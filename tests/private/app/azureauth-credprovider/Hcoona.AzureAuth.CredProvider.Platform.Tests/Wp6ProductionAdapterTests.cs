using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Runtime.InteropServices;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Contracts.AzureAuthDeployment;
using Hcoona.AzureAuth.CredProvider.Platform.AzureAuthProvider;
using Hcoona.AzureAuth.CredProvider.Platform.Composition;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Processes;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class Wp6ProductionAdapterTests
{
    public static bool IsLinux => OperatingSystem.IsLinux();

    [Fact(Skip = "Linux-specific secure-store integration.", SkipUnless = nameof(IsLinux))]
    public void SecureStoreCreatesReadsAndEnforcesCasWithFreshRevisions()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string rootPath = CreateTestDirectory();
        try
        {
            var store = new SystemAzureAuthSecureRecordStore(rootPath);
            AzureAuthSecureRecordWriteResult created = store.CompareExchange(
                "azureauth/provider-config.json",
                AzureAuthSecureRecordStoreContract.MissingRevision,
                Encoding.UTF8.GetBytes("""{"value":1}"""));
            AzureAuthSecureRecordReadResult read = store.Read(
                "azureauth/provider-config.json");
            AzureAuthSecureRecordRevisionCheckResult unchanged = store.CompareRevision(
                "azureauth/provider-config.json",
                created.Revision!);
            AzureAuthSecureRecordWriteResult stale = store.CompareExchange(
                "azureauth/provider-config.json",
                "stale-revision",
                Encoding.UTF8.GetBytes("""{"value":2}"""));
            AzureAuthSecureRecordWriteResult replaced = store.CompareExchange(
                "azureauth/provider-config.json",
                created.Revision!,
                Encoding.UTF8.GetBytes("""{"value":2}"""));

            Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, created.Status);
            Assert.Equal(AzureAuthSecureRecordReadStatus.Present, read.Status);
            Assert.Equal("""{"value":1}""", read.GetUtf8String());
            Assert.Equal(
                AzureAuthSecureRecordRevisionCheckStatus.Match,
                unchanged.Status);
            Assert.Equal(AzureAuthSecureRecordWriteStatus.Conflict, stale.Status);
            Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, replaced.Status);
            Assert.NotEqual(created.Revision, replaced.Revision);
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(
                    Path.Combine(rootPath, "azureauth", "provider-config.json")));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact(Skip = "Linux-specific secure-store integration.", SkipUnless = nameof(IsLinux))]
    public async Task SecureStoreSerializesCasAcrossCanonicalRootVariantsInTheSameProcess()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string rootPath = CreateTestDirectory();
        try
        {
            var firstStore = new SystemAzureAuthSecureRecordStore(rootPath);
            var secondStore = new SystemAzureAuthSecureRecordStore(
                rootPath + Path.DirectorySeparatorChar);
            using var start = new Barrier(2);
            Task<AzureAuthSecureRecordWriteResult> first = Task.Run(
                () =>
                {
                    start.SignalAndWait(TestContext.Current.CancellationToken);
                    return firstStore.CompareExchange(
                        "azureauth/provider-config.json",
                        AzureAuthSecureRecordStoreContract.MissingRevision,
                        Encoding.UTF8.GetBytes("""{"writer":1}"""));
                },
                TestContext.Current.CancellationToken);
            Task<AzureAuthSecureRecordWriteResult> second = Task.Run(
                () =>
                {
                    start.SignalAndWait(TestContext.Current.CancellationToken);
                    return secondStore.CompareExchange(
                        "azureauth/provider-config.json",
                        AzureAuthSecureRecordStoreContract.MissingRevision,
                        Encoding.UTF8.GetBytes("""{"writer":2}"""));
                },
                TestContext.Current.CancellationToken);

            AzureAuthSecureRecordWriteResult[] results = await Task.WhenAll(first, second);

            Assert.Single(
                results,
                static result => result.Status == AzureAuthSecureRecordWriteStatus.Success);
            Assert.Single(
                results,
                static result => result.Status == AzureAuthSecureRecordWriteStatus.Conflict);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact(Skip = "Linux-specific secure-store integration.", SkipUnless = nameof(IsLinux))]
    public async Task SecureStoreSerializesReadAndRevisionCheckWithWriterAcrossInstances()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string rootPath = CreateTestDirectory();
        var fileLock = new CoordinatedFileLock();
        try
        {
            var writerStore = new SystemAzureAuthSecureRecordStore(
                new SystemAzureAuthSecureRecordStoreOptions
                {
                    ConfigRootPath = rootPath,
                    FileLock = fileLock,
                });
            var readerStore = new SystemAzureAuthSecureRecordStore(
                new SystemAzureAuthSecureRecordStoreOptions
                {
                    ConfigRootPath = rootPath + Path.DirectorySeparatorChar,
                    FileLock = fileLock,
                });
            using var operationsStarted = new CountdownEvent(2);
            Task<AzureAuthSecureRecordWriteResult> writer = Task.Run(
                () => writerStore.CompareExchange(
                    "azureauth/provider-config.json",
                    AzureAuthSecureRecordStoreContract.MissingRevision,
                    Encoding.UTF8.GetBytes("""{"writer":1}""")),
                TestContext.Current.CancellationToken);
            Assert.True(
                fileLock.FirstLockAcquired.Wait(
                    TimeSpan.FromSeconds(2),
                    TestContext.Current.CancellationToken));
            Task<AzureAuthSecureRecordReadResult> read = Task.Run(
                () =>
                {
                    operationsStarted.Signal();
                    return readerStore.Read("azureauth/provider-config.json");
                },
                TestContext.Current.CancellationToken);
            Task<AzureAuthSecureRecordRevisionCheckResult> compared = Task.Run(
                () =>
                {
                    operationsStarted.Signal();
                    return readerStore.CompareRevision(
                        "azureauth/provider-config.json",
                        AzureAuthSecureRecordStoreContract.MissingRevision);
                },
                TestContext.Current.CancellationToken);
            Assert.True(
                operationsStarted.Wait(
                    TimeSpan.FromSeconds(2),
                    TestContext.Current.CancellationToken));

            await Task.Delay(100, TestContext.Current.CancellationToken);

            int attemptsWhileWriterHeldLock = fileLock.Attempts;
            bool readCompletedWhileWriterHeldLock = read.IsCompleted;
            bool compareCompletedWhileWriterHeldLock = compared.IsCompleted;
            fileLock.ReleaseFirstLock.Set();

            AzureAuthSecureRecordWriteResult written = await writer;
            AzureAuthSecureRecordReadResult readResult = await read;
            AzureAuthSecureRecordRevisionCheckResult comparedResult = await compared;

            Assert.Equal(1, attemptsWhileWriterHeldLock);
            Assert.False(readCompletedWhileWriterHeldLock);
            Assert.False(compareCompletedWhileWriterHeldLock);
            Assert.Equal(AzureAuthSecureRecordWriteStatus.Success, written.Status);
            Assert.Equal(AzureAuthSecureRecordReadStatus.Present, readResult.Status);
            Assert.Equal("""{"writer":1}""", readResult.GetUtf8String());
            Assert.Equal(
                AzureAuthSecureRecordRevisionCheckStatus.Conflict,
                comparedResult.Status);
            Assert.Equal(3, fileLock.Attempts);
        }
        finally
        {
            fileLock.ReleaseFirstLock.Set();
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact(Skip = "Linux-specific secure-store integration.", SkipUnless = nameof(IsLinux))]
    public void SecureStoreRejectsOversizedEnvelopeBeforeReadingIt()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string rootPath = CreateTestDirectory();
        try
        {
            string recordPath = CreateRawRecordPath(rootPath);
            File.WriteAllBytes(
                recordPath,
                new byte[SystemAzureAuthSecureRecordStore.MaximumEnvelopeBytesForTesting + 1]);
            File.SetUnixFileMode(
                recordPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);

            AzureAuthSecureRecordReadResult result =
                new SystemAzureAuthSecureRecordStore(rootPath)
                    .Read("azureauth/provider-config.json");

            Assert.Equal(AzureAuthSecureRecordReadStatus.Unsafe, result.Status);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact(Skip = "Linux-specific secure-store integration.", SkipUnless = nameof(IsLinux))]
    public void SecureStoreRejectsEnvelopeWhoseDecodedContentExceedsOneMiB()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string rootPath = CreateTestDirectory();
        try
        {
            string content = Convert.ToBase64String(
                new byte[SystemAzureAuthSecureRecordStore.MaximumRecordBytesForTesting + 1]);
            string recordPath = CreateRawRecordPath(rootPath);
            File.WriteAllText(
                recordPath,
                $$"""{"Revision":"revision-1","Content":"{{content}}"}""",
                new UTF8Encoding(false, true));
            File.SetUnixFileMode(
                recordPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);

            AzureAuthSecureRecordReadResult result =
                new SystemAzureAuthSecureRecordStore(rootPath)
                    .Read("azureauth/provider-config.json");

            Assert.Equal(AzureAuthSecureRecordReadStatus.Unsafe, result.Status);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact(Skip = "Linux-specific secure-store integration.", SkipUnless = nameof(IsLinux))]
    public void SecureStoreRetriesLockContentionThenRereadsRevision()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string rootPath = CreateTestDirectory();
        try
        {
            var initialStore = new SystemAzureAuthSecureRecordStore(rootPath);
            Assert.Equal(
                AzureAuthSecureRecordWriteStatus.Success,
                initialStore.CompareExchange(
                    "azureauth/provider-config.json",
                    AzureAuthSecureRecordStoreContract.MissingRevision,
                    Encoding.UTF8.GetBytes("{}")).Status);
            var fileLock = new ContendedFileLock(failuresBeforeSuccess: 2);
            var contendedStore = new SystemAzureAuthSecureRecordStore(
                new SystemAzureAuthSecureRecordStoreOptions
                {
                    ConfigRootPath = rootPath,
                    FileLock = fileLock,
                });

            AzureAuthSecureRecordWriteResult result = contendedStore.CompareExchange(
                "azureauth/provider-config.json",
                AzureAuthSecureRecordStoreContract.MissingRevision,
                Encoding.UTF8.GetBytes("""{"unexpected":true}"""));

            Assert.Equal(AzureAuthSecureRecordWriteStatus.Conflict, result.Status);
            Assert.Equal(3, fileLock.Attempts);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact(Skip = "Linux-specific secure-store integration.", SkipUnless = nameof(IsLinux))]
    public async Task SecureStoreLockTimeoutReturnsStableUnavailableResults()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string rootPath = CreateTestDirectory();
        try
        {
            var store = new SystemAzureAuthSecureRecordStore(
                new SystemAzureAuthSecureRecordStoreOptions
                {
                    ConfigRootPath = rootPath,
                    FileLock = new ContendedFileLock(failuresBeforeSuccess: int.MaxValue),
                    LockTimeout = TimeSpan.FromMilliseconds(40),
                });

            Task<(AzureAuthSecureRecordReadResult Read,
                AzureAuthSecureRecordRevisionCheckResult Compared,
                AzureAuthSecureRecordWriteResult Written)> operation = Task.Run(
                () => (
                    store.Read("azureauth/provider-config.json"),
                    store.CompareRevision(
                        "azureauth/provider-config.json",
                        AzureAuthSecureRecordStoreContract.MissingRevision),
                    store.CompareExchange(
                        "azureauth/provider-config.json",
                        AzureAuthSecureRecordStoreContract.MissingRevision,
                        Encoding.UTF8.GetBytes("{}"))),
                TestContext.Current.CancellationToken);
            var results = await operation.WaitAsync(
                TimeSpan.FromSeconds(2),
                TestContext.Current.CancellationToken);

            Assert.Equal(AzureAuthSecureRecordReadStatus.Unavailable, results.Read.Status);
            Assert.Equal(
                AzureAuthSecureRecordRevisionCheckStatus.Unavailable,
                results.Compared.Status);
            Assert.Equal(
                AzureAuthSecureRecordWriteStatus.Unavailable,
                results.Written.Status);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact(Skip = "Linux-specific secure-store integration.", SkipUnless = nameof(IsLinux))]
    public void SecureStoreReadAndCompareRevisionRejectRootThatIsAFile()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string parentPath = CreateTestDirectory();
        string rootPath = Path.Combine(parentPath, "records");
        try
        {
            File.WriteAllText(rootPath, "not a directory");
            var store = new SystemAzureAuthSecureRecordStore(rootPath);

            AzureAuthSecureRecordReadResult read = store.Read(
                "azureauth/provider-config.json");
            AzureAuthSecureRecordRevisionCheckResult compared = store.CompareRevision(
                "azureauth/provider-config.json",
                AzureAuthSecureRecordStoreContract.MissingRevision);

            Assert.Equal(AzureAuthSecureRecordReadStatus.Unsafe, read.Status);
            Assert.Equal(
                AzureAuthSecureRecordRevisionCheckStatus.Unsafe,
                compared.Status);
        }
        finally
        {
            Directory.Delete(parentPath, recursive: true);
        }
    }

    [Fact(Skip = "Linux-specific secure-store integration.", SkipUnless = nameof(IsLinux))]
    public void SecureStoreReadAndCompareRevisionRejectIntermediateThatIsAFile()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string rootPath = CreateTestDirectory();
        try
        {
            string intermediatePath = Path.Combine(rootPath, "azureauth");
            File.WriteAllText(intermediatePath, "not a directory");
            File.SetUnixFileMode(
                intermediatePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
            var store = new SystemAzureAuthSecureRecordStore(rootPath);

            AzureAuthSecureRecordReadResult read = store.Read(
                "azureauth/provider-config.json");
            AzureAuthSecureRecordRevisionCheckResult compared = store.CompareRevision(
                "azureauth/provider-config.json",
                AzureAuthSecureRecordStoreContract.MissingRevision);

            Assert.Equal(AzureAuthSecureRecordReadStatus.Unsafe, read.Status);
            Assert.Equal(
                AzureAuthSecureRecordRevisionCheckStatus.Unsafe,
                compared.Status);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact(Skip = "Linux-specific secure-store integration.", SkipUnless = nameof(IsLinux))]
    public async Task SecureStoreRejectsFifoRecordWithoutOpeningOrBlocking()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string rootPath = CreateTestDirectory();
        try
        {
            string recordDirectory = Path.Combine(rootPath, "azureauth");
            Directory.CreateDirectory(
                recordDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            string recordPath = Path.Combine(recordDirectory, "provider-config.json");
            Assert.Equal(0, MkFifo(recordPath, Convert.ToUInt32("600", 8)));
            var store = new SystemAzureAuthSecureRecordStore(rootPath);

            AzureAuthSecureRecordReadResult result = await Task.Run(
                    () => store.Read("azureauth/provider-config.json"),
                    TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

            Assert.Equal(AzureAuthSecureRecordReadStatus.Unsafe, result.Status);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact(Skip = "Linux-specific secure-store integration.", SkipUnless = nameof(IsLinux))]
    public void SecureStoreRejectsDirectoryInPlaceOfRecord()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string rootPath = CreateTestDirectory();
        try
        {
            string recordPath = Path.Combine(rootPath, "azureauth", "provider-config.json");
            Directory.CreateDirectory(
                recordPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var store = new SystemAzureAuthSecureRecordStore(rootPath);

            AzureAuthSecureRecordReadResult result = store.Read(
                "azureauth/provider-config.json");

            Assert.Equal(AzureAuthSecureRecordReadStatus.Unsafe, result.Status);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact(Skip = "Linux-specific secure-store integration.", SkipUnless = nameof(IsLinux))]
    public void SecureStoreRejectsReplaceableAncestorButAcceptsStandardStickyTemporaryAncestor()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string parentPath = CreateTestDirectory();
        try
        {
            string replaceableAncestor = Path.Combine(parentPath, "replaceable");
            Directory.CreateDirectory(replaceableAncestor);
            File.SetUnixFileMode(
                replaceableAncestor,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupWrite
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherWrite
                    | UnixFileMode.OtherExecute);
            string unsafeRoot = Path.Combine(replaceableAncestor, "azureauth-credprovider");
            Directory.CreateDirectory(
                unsafeRoot,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

            Assert.Equal(
                AzureAuthSecureRecordReadStatus.Unsafe,
                new SystemAzureAuthSecureRecordStore(unsafeRoot)
                    .Read("azureauth/provider-config.json").Status);

            string stickyAncestor = Path.Combine(parentPath, "sticky");
            Directory.CreateDirectory(stickyAncestor);
            File.SetUnixFileMode(
                stickyAncestor,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupWrite
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherWrite
                    | UnixFileMode.OtherExecute
                    | UnixFileMode.StickyBit);
            string safeRoot = Path.Combine(stickyAncestor, "azureauth-credprovider");
            Directory.CreateDirectory(
                safeRoot,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var store = new SystemAzureAuthSecureRecordStore(safeRoot);

            Assert.Equal(
                AzureAuthSecureRecordWriteStatus.Success,
                store.CompareExchange(
                    "azureauth/provider-config.json",
                    AzureAuthSecureRecordStoreContract.MissingRevision,
                    Encoding.UTF8.GetBytes("{}")).Status);
        }
        finally
        {
            Directory.Delete(parentPath, recursive: true);
        }
    }

    [Theory(Skip = "Linux-specific secure-store integration.", SkipUnless = nameof(IsLinux))]
    [InlineData(false)]
    [InlineData(true)]
    public void SecureStoreRejectsAncestorOwnedByAnotherUserEvenWithoutReplaceableModes(
        bool sticky)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string parentPath = CreateTestDirectory();
        try
        {
            string ancestorPath = Path.Combine(parentPath, "foreign-owned");
            Directory.CreateDirectory(ancestorPath);
            UnixFileMode mode =
                UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherExecute;
            if (sticky)
            {
                mode =
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
            }

            File.SetUnixFileMode(ancestorPath, mode);
            string rootPath = Path.Combine(ancestorPath, "azureauth-credprovider");
            Directory.CreateDirectory(
                rootPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var metadata = new ForeignOwnerMetadataProvider(ancestorPath);
            var store = new SystemAzureAuthSecureRecordStore(
                new SystemAzureAuthSecureRecordStoreOptions
                {
                    ConfigRootPath = rootPath,
                    LinuxFileMetadataProvider = metadata,
                });

            AzureAuthSecureRecordReadResult result = store.Read(
                "azureauth/provider-config.json");

            Assert.Equal(AzureAuthSecureRecordReadStatus.Unsafe, result.Status);
        }
        finally
        {
            Directory.Delete(parentPath, recursive: true);
        }
    }
    [Fact(Skip = "Linux-specific secure-store integration.", SkipUnless = nameof(IsLinux))]
    public void SecureStoreRejectsLinkedRecordParent()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string rootPath = CreateTestDirectory();
        string outsidePath = CreateTestDirectory();
        try
        {
            Directory.CreateSymbolicLink(Path.Combine(rootPath, "azureauth"), outsidePath);
            var store = new SystemAzureAuthSecureRecordStore(rootPath);

            AzureAuthSecureRecordWriteResult result = store.CompareExchange(
                "azureauth/provider-config.json",
                AzureAuthSecureRecordStoreContract.MissingRevision,
                Encoding.UTF8.GetBytes("{}"));

            Assert.Equal(AzureAuthSecureRecordWriteStatus.Unsafe, result.Status);
            Assert.False(File.Exists(Path.Combine(outsidePath, "provider-config.json")));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
            Directory.Delete(outsidePath, recursive: true);
        }
    }

    [Fact(Skip = "Linux-specific secure-store integration.", SkipUnless = nameof(IsLinux))]
    public void SecureStoreRejectsPreviouslyExposedRootAndRecordPermissions()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string rootPath = CreateTestDirectory();
        try
        {
            File.SetUnixFileMode(
                rootPath,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead);
            var unsafeRootStore = new SystemAzureAuthSecureRecordStore(rootPath);
            Assert.Equal(
                AzureAuthSecureRecordWriteStatus.Unsafe,
                unsafeRootStore.CompareExchange(
                    "azureauth/provider-config.json",
                    AzureAuthSecureRecordStoreContract.MissingRevision,
                    Encoding.UTF8.GetBytes("{}")).Status);
            Assert.True((File.GetUnixFileMode(rootPath) & UnixFileMode.GroupRead) != 0);

            File.SetUnixFileMode(
                rootPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var store = new SystemAzureAuthSecureRecordStore(rootPath);
            AzureAuthSecureRecordWriteResult created = store.CompareExchange(
                "azureauth/provider-config.json",
                AzureAuthSecureRecordStoreContract.MissingRevision,
                Encoding.UTF8.GetBytes("{}"));
            string recordPath = Path.Combine(rootPath, "azureauth", "provider-config.json");
            File.SetUnixFileMode(
                recordPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherRead);

            Assert.Equal(AzureAuthSecureRecordReadStatus.Unsafe, store.Read(
                "azureauth/provider-config.json").Status);
            Assert.Equal(
                AzureAuthSecureRecordWriteStatus.Unsafe,
                store.CompareExchange(
                    "azureauth/provider-config.json",
                    created.Revision!,
                    Encoding.UTF8.GetBytes("""{"changed":true}""")).Status);
            Assert.True((File.GetUnixFileMode(recordPath) & UnixFileMode.OtherRead) != 0);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact(Skip = "Linux-specific secure-store integration.", SkipUnless = nameof(IsLinux))]
    public void SecureStoreFlushesParentDirectoryAndFailsUnsupportedDurabilityBeforeMutation()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string rootPath = CreateTestDirectory();
        try
        {
            var durability = new RecordingDirectoryDurability();
            var store = new SystemAzureAuthSecureRecordStore(
                new SystemAzureAuthSecureRecordStoreOptions
                {
                    ConfigRootPath = rootPath,
                    DirectoryDurability = durability,
                });
            Assert.Equal(
                AzureAuthSecureRecordWriteStatus.Success,
                store.CompareExchange(
                    "azureauth/provider-config.json",
                    AzureAuthSecureRecordStoreContract.MissingRevision,
                    Encoding.UTF8.GetBytes("{}")).Status);
            Assert.Equal(
                [
                    Path.Combine(rootPath, "azureauth"),
                    rootPath,
                    Path.Combine(rootPath, "azureauth"),
                ],
                durability.Paths);

            string unsupportedRoot = Path.Combine(rootPath, "unsupported");
            var unsupported = new SystemAzureAuthSecureRecordStore(
                new SystemAzureAuthSecureRecordStoreOptions
                {
                    ConfigRootPath = unsupportedRoot,
                    DirectoryDurability = new RecordingDirectoryDurability(isSupported: false),
                });
            Assert.Equal(
                AzureAuthSecureRecordWriteStatus.Unsupported,
                unsupported.CompareExchange(
                    "azureauth/provider-config.json",
                    AzureAuthSecureRecordStoreContract.MissingRevision,
                    Encoding.UTF8.GetBytes("{}")).Status);
            Assert.False(Directory.Exists(unsupportedRoot));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    [Fact(Skip = "Linux-specific secure-store integration.", SkipUnless = nameof(IsLinux))]
    public void SecureStoreDurablyFlushesEveryNewDirectoryAndParentEntry()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string parentPath = CreateTestDirectory();
        string rootPath = Path.Combine(parentPath, "product", "records");
        try
        {
            var durability = new RecordingDirectoryDurability();
            var store = new SystemAzureAuthSecureRecordStore(
                new SystemAzureAuthSecureRecordStoreOptions
                {
                    ConfigRootPath = rootPath,
                    DirectoryDurability = durability,
                });

            Assert.Equal(
                AzureAuthSecureRecordWriteStatus.Success,
                store.CompareExchange(
                    "azureauth/provider-config.json",
                    AzureAuthSecureRecordStoreContract.MissingRevision,
                    Encoding.UTF8.GetBytes("{}")).Status);

            string productPath = Path.Combine(parentPath, "product");
            string recordDirectory = Path.Combine(rootPath, "azureauth");
            Assert.Equal(
                [
                    productPath,
                    parentPath,
                    rootPath,
                    productPath,
                    recordDirectory,
                    rootPath,
                    recordDirectory,
                ],
                durability.Paths);
        }
        finally
        {
            Directory.Delete(parentPath, recursive: true);
        }
    }

    [Fact]
    public async Task WindowsProbeUsesFixedPowerShellAndExplicitTargetEnvironment()
    {
        string mountRoot = CreateTestDirectory();
        try
        {
            string powerShellPath = Path.Combine(
                mountRoot,
                "Windows",
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(powerShellPath)!);
            File.WriteAllText(powerShellPath, string.Empty);
            AzureAuthProviderConfig config = CreateConfig();
            var runner = new ProbeProcessRunner(config.DeploymentConfig!);
            var probe = new SystemWindowsArtifactProbe(
                runner,
                new SystemWindowsArtifactProbeOptions
                {
                    WindowsMountRoot = mountRoot,
                    WslInterop = "/run/WSL/123_interop",
                    EnvironmentVariableReader = _ => throw new InvalidOperationException(),
                });

            WindowsArtifactProbeResult result = probe.Probe(config.DeploymentConfig!);

            Assert.Equal(AzureAuthArtifactTrustStatus.Trusted, result.Status);
            Assert.Equal(powerShellPath, runner.StartSpec!.FileName);
            Assert.Equal(
                Path.Combine(mountRoot, "Windows", "System32"),
                runner.StartSpec.WorkingDirectory);
            Assert.NotNull(runner.StartSpec.PreStartValidation);
            await runner.StartSpec.PreStartValidation(TestContext.Current.CancellationToken);
            Assert.Equal(ProcessEnvironmentMode.ExplicitOnly, runner.StartSpec.EnvironmentMode);
            AzureAuthDeploymentConfig deployment = config.DeploymentConfig!;
            Assert.Equal(
                deployment.ExecutablePath,
                runner.StartSpec.Environment["AZUREAUTH_PROBE_TARGET"]);
            Assert.Equal(
                "/run/WSL/123_interop",
                runner.StartSpec.Environment["WSL_INTEROP"]);
            Assert.Equal(@"C:\Windows\System32", runner.StartSpec.Environment["PATH"]);
            Assert.Equal(
                runner.StartSpec.Environment.Keys
                    .Where(static key => key is not "WSLENV" and not "WSL_INTEROP")
                    .OrderBy(static key => key, StringComparer.Ordinal),
                runner.StartSpec.Environment["WSLENV"]!
                    .Split(':')
                    .OrderBy(static key => key, StringComparer.Ordinal));
            Assert.Equal(string.Empty, runner.StartSpec.Environment["DOTNET_ROOT"]);
            Assert.Equal(string.Empty, runner.StartSpec.Environment["SYSTEM_ACCESSTOKEN"]);
            string[] clrInjectionVariables =
            [
                "CORECLR_ENABLE_PROFILING",
                "CORECLR_PROFILER",
                "CORECLR_PROFILER_PATH",
                "CORECLR_PROFILER_PATH_32",
                "CORECLR_PROFILER_PATH_64",
                "COR_ENABLE_PROFILING",
                "COR_PROFILER",
                "COR_PROFILER_PATH",
                "COR_PROFILER_PATH_32",
                "COR_PROFILER_PATH_64",
                "DOTNET_STARTUP_HOOKS",
            ];
            Assert.All(
                clrInjectionVariables,
                key => Assert.Equal(string.Empty, runner.StartSpec.Environment[key]));
            string[] bridged = runner.StartSpec.Environment["WSLENV"]!.Split(':');
            Assert.All(clrInjectionVariables, key => Assert.Contains(key, bridged));
            Assert.DoesNotContain(
                deployment.ExecutablePath,
                runner.StartSpec.Arguments[^1],
                StringComparison.Ordinal);
            Assert.Equal(@"C:\Tools", result.Evidence!.TrustedExecutableDirectory);
            Assert.Equal(@"C:\Windows\System32", result.Evidence.TrustedSystemDirectory);
            Assert.Equal(@"C:\Windows\System32", result.Evidence.TrustedWorkingDirectory);
            Assert.Equal([@"C:\Windows\System32"], result.Evidence.TrustedPathEntries);
        }
        finally
        {
            Directory.Delete(mountRoot, recursive: true);
        }
    }

    [Fact]
    [UnconditionalSuppressMessage(
        "Interoperability",
        "CA1416:Validate platform compatibility",
        Justification = "The test only evaluates Windows ACL enum values and probe script text.")]
    public void WindowsProbeAclPolicySeparatesReadExecuteFromMutationAndChecksTrustedOwners()
    {
        const System.Security.AccessControl.FileSystemRights MutationRights =
            System.Security.AccessControl.FileSystemRights.WriteData
            | System.Security.AccessControl.FileSystemRights.CreateFiles
            | System.Security.AccessControl.FileSystemRights.AppendData
            | System.Security.AccessControl.FileSystemRights.CreateDirectories
            | System.Security.AccessControl.FileSystemRights.WriteAttributes
            | System.Security.AccessControl.FileSystemRights.WriteExtendedAttributes
            | System.Security.AccessControl.FileSystemRights.Delete
            | System.Security.AccessControl.FileSystemRights.DeleteSubdirectoriesAndFiles
            | System.Security.AccessControl.FileSystemRights.ChangePermissions
            | System.Security.AccessControl.FileSystemRights.TakeOwnership;

        Assert.Equal(
            0,
            (int)(MutationRights
                & System.Security.AccessControl.FileSystemRights.ReadAndExecute));
        string script = SystemWindowsArtifactProbe.ProbeScriptForTesting;
        Assert.DoesNotContain("::FullControl", script, StringComparison.Ordinal);
        Assert.DoesNotContain("::Modify", script, StringComparison.Ordinal);
        Assert.Contains("::WriteData", script, StringComparison.Ordinal);
        Assert.Contains("::ChangePermissions", script, StringComparison.Ordinal);
        Assert.Contains("::TakeOwnership", script, StringComparison.Ordinal);
        Assert.Contains("$isInspectedDirectory=$true", script, StringComparison.Ordinal);
        Assert.Contains(
            "$script:ancestorMutationMask=",
            script,
            StringComparison.Ordinal);
        Assert.Contains("'S-1-5-18'", script, StringComparison.Ordinal);
        Assert.Contains("'S-1-5-32-544'", script, StringComparison.Ordinal);
        Assert.Contains("'NT SERVICE','TrustedInstaller'", script, StringComparison.Ordinal);
        Assert.Contains("$script:allowed -notcontains $ownerSid", script, StringComparison.Ordinal);
        Assert.Contains("$script:allowed -notcontains $sid", script, StringComparison.Ordinal);
        const string EffectiveAceCheck =
            "$rule.PropagationFlags -band "
            + "[Security.AccessControl.PropagationFlags]::InheritOnly) -eq 0";
        Assert.Equal(2, script.Split(EffectiveAceCheck, StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void WindowsProbeRejectsNullDaclButDoesNotTreatEmptyPresentDaclAsWritable()
    {
        Assert.True(
            IsDaclAutomaticallyUnsafe(daclPresent: false, discretionaryAcl: null));
        Assert.True(IsDaclAutomaticallyUnsafe(daclPresent: true, discretionaryAcl: null));
        Assert.False(
            IsDaclAutomaticallyUnsafe(
                daclPresent: true,
                discretionaryAcl: Array.Empty<object>()));

        string script = SystemWindowsArtifactProbe.ProbeScriptForTesting;
        Assert.Contains("$rawDescriptor.ControlFlags", script, StringComparison.Ordinal);
        Assert.Contains(
            "[Security.AccessControl.ControlFlags]::DiscretionaryAclPresent",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "$null -ne $rawDescriptor.DiscretionaryAcl",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "-or -not $executableDaclPresentAndNonNull",
            script,
            StringComparison.Ordinal);
        Assert.Contains(
            "$safeAcl -and $safeDacls",
            script,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        System.Security.AccessControl.FileSystemRights.ReadAndExecute,
        System.Security.AccessControl.PropagationFlags.InheritOnly,
        true,
        false)]
    [InlineData(
        System.Security.AccessControl.FileSystemRights.FullControl,
        System.Security.AccessControl.PropagationFlags.InheritOnly,
        true,
        false)]
    [InlineData(
        System.Security.AccessControl.FileSystemRights.ReadAndExecute,
        System.Security.AccessControl.PropagationFlags.None,
        true,
        false)]
    [InlineData(
        System.Security.AccessControl.FileSystemRights.CreateFiles,
        System.Security.AccessControl.PropagationFlags.None,
        false,
        false)]
    [InlineData(
        System.Security.AccessControl.FileSystemRights.CreateDirectories,
        System.Security.AccessControl.PropagationFlags.None,
        false,
        false)]
    [InlineData(
        System.Security.AccessControl.FileSystemRights.CreateFiles,
        System.Security.AccessControl.PropagationFlags.None,
        true,
        true)]
    [InlineData(
        System.Security.AccessControl.FileSystemRights.DeleteSubdirectoriesAndFiles,
        System.Security.AccessControl.PropagationFlags.None,
        false,
        true)]
    [InlineData(
        System.Security.AccessControl.FileSystemRights.Delete,
        System.Security.AccessControl.PropagationFlags.None,
        false,
        true)]
    [InlineData(
        System.Security.AccessControl.FileSystemRights.TakeOwnership,
        System.Security.AccessControl.PropagationFlags.None,
        false,
        true)]
    [UnconditionalSuppressMessage(
        "Interoperability",
        "CA1416:Validate platform compatibility",
        Justification = "The test only evaluates Windows ACL enum values.")]
    public void WindowsProbeAclPolicyUsesDirectoryObjectSemantics(
        System.Security.AccessControl.FileSystemRights rights,
        System.Security.AccessControl.PropagationFlags propagationFlags,
        bool isInspectedDirectory,
        bool expectedUnsafe)
    {
        const System.Security.AccessControl.FileSystemRights DirectoryMutationRights =
            System.Security.AccessControl.FileSystemRights.WriteData
            | System.Security.AccessControl.FileSystemRights.CreateFiles
            | System.Security.AccessControl.FileSystemRights.AppendData
            | System.Security.AccessControl.FileSystemRights.CreateDirectories
            | System.Security.AccessControl.FileSystemRights.WriteAttributes
            | System.Security.AccessControl.FileSystemRights.WriteExtendedAttributes
            | System.Security.AccessControl.FileSystemRights.Delete
            | System.Security.AccessControl.FileSystemRights.DeleteSubdirectoriesAndFiles
            | System.Security.AccessControl.FileSystemRights.ChangePermissions
            | System.Security.AccessControl.FileSystemRights.TakeOwnership;
        const System.Security.AccessControl.FileSystemRights AncestorMutationRights =
            System.Security.AccessControl.FileSystemRights.DeleteSubdirectoriesAndFiles
            | System.Security.AccessControl.FileSystemRights.Delete
            | System.Security.AccessControl.FileSystemRights.ChangePermissions
            | System.Security.AccessControl.FileSystemRights.TakeOwnership;

        bool effectiveOnInspectedPath =
            (propagationFlags & System.Security.AccessControl.PropagationFlags.InheritOnly) == 0;
        System.Security.AccessControl.FileSystemRights mutationRights = isInspectedDirectory
            ? DirectoryMutationRights
            : AncestorMutationRights;
        bool unsafeWrite = effectiveOnInspectedPath && (rights & mutationRights) != 0;

        Assert.Equal(expectedUnsafe, unsafeWrite);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("relative")]
    [InlineData("/run/WSL/../bad")]
    [InlineData("/run/WSL/not-a-socket")]
    [InlineData("/run/WSL/bad\n")]
    public void WindowsProbeFailsClosedForMissingOrInvalidWslInterop(string? wslInterop)
    {
        string mountRoot = CreateTestDirectory();
        try
        {
            string powerShellPath = Path.Combine(
                mountRoot,
                "Windows",
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(powerShellPath)!);
            File.WriteAllText(powerShellPath, string.Empty);
            var runner = new ProbeProcessRunner(CreateConfig().DeploymentConfig!);
            var probe = new SystemWindowsArtifactProbe(
                runner,
                new SystemWindowsArtifactProbeOptions
                {
                    WindowsMountRoot = mountRoot,
                    WslInterop = wslInterop,
                    EnvironmentVariableReader = _ => null,
                });

            Assert.Equal(
                AzureAuthArtifactTrustStatus.Deferred,
                probe.Probe(CreateConfig().DeploymentConfig!).Status);
            Assert.Equal(0, runner.CallCount);
        }
        finally
        {
            Directory.Delete(mountRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("""{"Trusted":true}""")]
    [InlineData("""{"trusted":false,"unknown":1}""")]
    [InlineData("""{"trusted":false,"trusted":false}""")]
    [InlineData("""{"trusted":"false"}""")]
    public void WindowsProbeRejectsNonStrictJson(string json)
    {
        string mountRoot = CreateTestDirectory();
        try
        {
            string powerShellPath = Path.Combine(
                mountRoot,
                "Windows",
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(powerShellPath)!);
            File.WriteAllText(powerShellPath, string.Empty);
            var probe = new SystemWindowsArtifactProbe(
                new RawProbeProcessRunner(json),
                new SystemWindowsArtifactProbeOptions
                {
                    WindowsMountRoot = mountRoot,
                    WslInterop = "/run/WSL/123_interop",
                    EnvironmentVariableReader = _ => null,
                });

            Assert.Equal(
                AzureAuthArtifactTrustStatus.Untrusted,
                probe.Probe(CreateConfig().DeploymentConfig!).Status);
        }
        finally
        {
            Directory.Delete(mountRoot, recursive: true);
        }
    }

    [Fact]
    public void WindowsProbeRequiresStrictDaclEvidenceField()
    {
        string mountRoot = CreateTestDirectory();
        try
        {
            string powerShellPath = Path.Combine(
                mountRoot,
                "Windows",
                "System32",
                "WindowsPowerShell",
                "v1.0",
                "powershell.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(powerShellPath)!);
            File.WriteAllText(powerShellPath, string.Empty);
            AzureAuthProviderConfig config = CreateConfig();
            var probe = new SystemWindowsArtifactProbe(
                new ProbeProcessRunner(config.DeploymentConfig!, includeDaclEvidence: false),
                new SystemWindowsArtifactProbeOptions
                {
                    WindowsMountRoot = mountRoot,
                    WslInterop = "/run/WSL/123_interop",
                    EnvironmentVariableReader = _ => null,
                });

            Assert.Equal(
                AzureAuthArtifactTrustStatus.Untrusted,
                probe.Probe(config.DeploymentConfig!).Status);
        }
        finally
        {
            Directory.Delete(mountRoot, recursive: true);
        }
    }

    [Fact]
    public void TrustPolicyRejectsAbsentOrNullDaclEvidence()
    {
        AzureAuthProviderConfig config = CreateConfig();
        AzureAuthArtifactEvidence evidence = CreateEvidence(config.DeploymentConfig!) with
        {
            DiscretionaryAclsPresentAndNonNull = false,
        };

        AzureAuthTrustResult result = AzureAuthTrustPolicy.Evaluate(
            config.DeploymentConfig!,
            new WslWindowsArtifactTrustInspector(new EvidenceProbe(evidence)));

        Assert.Equal(AzureAuthArtifactTrustStatus.Untrusted, result.Status);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void TrustPolicyRejectsReparseOrWritableExecutableDirectoryChain(
        bool noReparsePoints,
        bool ownerOnlyWritable)
    {
        AzureAuthProviderConfig config = CreateConfig();
        AzureAuthArtifactEvidence evidence = CreateEvidence(config.DeploymentConfig!) with
        {
            ExecutableDirectoryChainHasNoReparsePoints = noReparsePoints,
            ExecutableDirectoryChainOwnerOnlyWritable = ownerOnlyWritable,
        };

        AzureAuthTrustResult result = AzureAuthTrustPolicy.Evaluate(
            config.DeploymentConfig!,
            new WslWindowsArtifactTrustInspector(new EvidenceProbe(evidence)));

        Assert.Equal(AzureAuthArtifactTrustStatus.Untrusted, result.Status);
    }

    [Fact(Skip = "Linux-specific production composition.", SkipUnless = nameof(IsLinux))]
    public void ProductionCompositionLoadsPersistedAzureAuthAndDiscoversWslLaunch()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string rootPath = CreateTestDirectory();
        try
        {
            var store = new SystemAzureAuthSecureRecordStore(rootPath);
            AzureAuthProviderConfig config = CreateConfig();
            var probe = new TrustedProbe(config.DeploymentConfig!);
            var inspector = new WslWindowsArtifactTrustInspector(probe);
            AzureAuthTrustResult trust = AzureAuthTrustPolicy.Evaluate(
                config.DeploymentConfig!,
                inspector);
            AzureAuthBinding binding = AzureAuthBindingPolicy.CreateBound(
                config,
                "user@example.invalid",
                "tenant-1",
                DateTimeOffset.FromUnixTimeSeconds(
                    DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds()),
                trust);
            Assert.Equal(
                AzureAuthSecureRecordWriteStatus.Success,
                new AzureAuthProviderConfigPersistence(store)
                    .Create(CredentialProviderCompositionRoot.ProviderConfigRecordName, config)
                    .Status);
            Assert.Equal(
                AzureAuthSecureRecordWriteStatus.Success,
                new AzureAuthBindingPersistence(store)
                    .Bind(
                        AzureAuthPersistedRecord<AzureAuthBinding>.Missing(
                            CredentialProviderCompositionRoot.BindingRecordName),
                        config,
                        binding.AccountId!,
                        binding.TenantId!,
                        binding.RecordedAtUtc,
                        trust)
                    .Status);
            var processRunner = new TokenProcessRunner();

            CredentialProviderCompositionRoot root =
                CredentialProviderCompositionRoot.CreateProduction(
                    new CredentialProviderProductionOptions
                    {
                        SecureRecordStore = store,
                        WindowsArtifactProbe = probe,
                        ProcessRunner = processRunner,
                        EnvironmentVariableReader = name =>
                            name == "WSL_INTEROP" ? "/run/WSL/123_interop" : null,
                    });
            CredentialResult acquired = root.Boundary.Acquire(
                CreateRequest(),
                TestContext.Current.CancellationToken);

            Assert.False(root.Readiness.Interactive.IsReady);
            Assert.Equal("AccountEnforcementUnavailable", root.Readiness.Interactive.Code);
            Assert.Equal(CredentialResultStatus.CredentialUnavailable, acquired.Status);
            Assert.Equal("AccountEnforcementUnavailable", acquired.Error?.Code);
            Assert.Equal(0, processRunner.CallCount);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private static string CreateTestDirectory()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "wp6-production-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return path;
    }

    private static string CreateRawRecordPath(string rootPath)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException();
        }

        string directoryPath = Path.Combine(rootPath, "azureauth");
        Directory.CreateDirectory(
            directoryPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return Path.Combine(directoryPath, "provider-config.json");
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        uint mode);

    private static AzureAuthProviderConfig CreateConfig() =>
        AzureAuthProviderConfig.CreateAzureAuth(
            new AzureAuthDeploymentConfig
            {
                SchemaVersion = ContractVersions.AzureAuthDeploymentConfigSchemaMajor,
                ExecutablePath = @"C:\Tools\AzureAuth.exe",
                ExecutableSha256 = new string('a', 64),
                SignerIdentity = "CN=AzureAuth, O=Example, C=US",
                PublisherName = "AzureAuth",
                ExecutableVersion = "1.2.3.4",
                ProvenanceIdentifier = "example/wp6",
            });

    private static CredentialRequestV2 CreateRequest() =>
        new()
        {
            Ecosystem = CredentialEcosystem.Git,
            Operation = CredentialOperation.Get,
            Resource = CanonicalResourceIdentity.Create(
                "dev.azure.com",
                "example",
                new Uri("https://dev.azure.com/example")),
            ServiceIdentity = "default",
            RequestedAudience = TokenAudience.AzureDevOps,
            CredentialKind = CredentialKind.BasicPassword,
            IdentityFlow = IdentityFlow.InteractiveBrowser,
            InteractivePolicy = InteractivePolicy.UserAllowed,
            AcquisitionMode = AcquisitionMode.InteractionAllowed,
            CachePolicy = CachePolicyMode.ProductPersistentCacheDisabled,
        };

    private static AzureAuthArtifactEvidence CreateEvidence(
        AzureAuthDeploymentConfig config) =>
        new()
        {
            CanonicalPath = config.ExecutablePath,
            StableArtifactIdentity = new FileSystemEntryIdentity("artifact-1"),
            Sha256Hash = config.ExecutableSha256,
            SignerIdentity = config.SignerIdentity,
            PublisherName = config.PublisherName,
            ExecutableVersion = config.ExecutableVersion,
            ProvenanceIdentifier = config.ProvenanceIdentifier,
            Owner = new FileSystemOwner("current-user"),
            CurrentUserOwnsArtifact = true,
            OwnerOnlyWritable = true,
            DiscretionaryAclsPresentAndNonNull = true,
            TrustedExecutableDirectory = @"C:\Tools",
            ExecutableDirectoryChainHasNoReparsePoints = true,
            ExecutableDirectoryChainOwnerOnlyWritable = true,
            TrustedSystemDirectory = @"C:\Windows\System32",
            SystemDirectoryChainHasNoReparsePoints = true,
            SystemDirectoryChainOwnerOnlyWritable = true,
            TrustedWorkingDirectory = @"C:\Windows\System32",
            TrustedPathEntries = [@"C:\Windows\System32"],
        };

    private static string CreateJwt()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string header = Base64Url("""{"alg":"RS256","typ":"JWT"}""");
        string payload = Base64Url(
            $$"""{"aud":"499b84ac-1321-427f-aa17-267ca6975798","tid":"tenant-1","iat":{{now.AddMinutes(-1).ToUnixTimeSeconds()}},"nbf":{{now.AddMinutes(-1).ToUnixTimeSeconds()}},"exp":{{now.AddHours(1).ToUnixTimeSeconds()}}}""");
        return $"{header}.{payload}.c2lnbmF0dXJl";
    }

    private static string Base64Url(string value) =>
        Convert
            .ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static bool IsDaclAutomaticallyUnsafe(
        bool daclPresent,
        object? discretionaryAcl) =>
        !daclPresent || discretionaryAcl is null;

    private sealed class TrustedProbe(AzureAuthDeploymentConfig config) : IWindowsArtifactProbe
    {
        public WindowsArtifactProbeResult Probe(AzureAuthDeploymentConfig _) =>
            WindowsArtifactProbeResult.Trusted(CreateEvidence(config));
    }

    private sealed class EvidenceProbe(AzureAuthArtifactEvidence evidence) : IWindowsArtifactProbe
    {
        public WindowsArtifactProbeResult Probe(AzureAuthDeploymentConfig _) =>
            WindowsArtifactProbeResult.Trusted(evidence);
    }

    private sealed class TokenProcessRunner : IProcessRunner
    {
        public int CallCount { get; private set; }
        public ProcessStartSpec? StartSpec { get; private set; }

        public async Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            StartSpec = startSpec;
            if (startSpec.PreStartValidation is not null)
            {
                await startSpec.PreStartValidation(cancellationToken);
            }

            return new ProcessResult(0, CreateJwt() + "\n", string.Empty);
        }
    }

    private sealed class ProbeProcessRunner(
        AzureAuthDeploymentConfig config,
        bool includeDaclEvidence = true)
        : IProcessRunner
    {
        public int CallCount { get; private set; }
        public ProcessStartSpec? StartSpec { get; private set; }

        public Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            StartSpec = startSpec;
            string json =
                $$"""{"trusted":true,"canonicalPath":"C:\\Tools\\AzureAuth.exe","stableArtifactIdentity":"artifact-1","sha256":"{{config.ExecutableSha256}}","signerIdentity":"{{config.SignerIdentity}}","publisherName":"{{config.PublisherName}}","executableVersion":"{{config.ExecutableVersion}}","provenanceIdentifier":"{{config.ProvenanceIdentifier}}","owner":"current-user","currentUserOwnsArtifact":true,"ownerOnlyWritable":true,"discretionaryAclsPresentAndNonNull":true,"trustedExecutableDirectory":"C:\\Tools","executableDirectoryChainHasNoReparsePoints":true,"executableDirectoryChainOwnerOnlyWritable":true,"trustedSystemDirectory":"C:\\Windows\\System32","systemDirectoryChainHasNoReparsePoints":true,"systemDirectoryChainOwnerOnlyWritable":true,"trustedWorkingDirectory":"C:\\Windows\\System32","trustedPathEntries":["C:\\Windows\\System32"]}""";
            if (!includeDaclEvidence)
            {
                json = json.Replace(
                    ""","discretionaryAclsPresentAndNonNull":true""",
                    string.Empty,
                    StringComparison.Ordinal);
            }

            return Task.FromResult(new ProcessResult(0, json, string.Empty));
        }
    }

    private sealed class RawProbeProcessRunner(string json) : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(
            ProcessStartSpec startSpec,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ProcessResult(0, json, string.Empty));
    }

    private sealed class RecordingDirectoryDurability(bool isSupported = true)
        : IAzureAuthDirectoryDurability
    {
        public bool IsSupported { get; } = isSupported;

        public List<string> Paths { get; } = [];

        public void Flush(string directoryPath) => Paths.Add(directoryPath);
    }

    private sealed class ContendedFileLock(int failuresBeforeSuccess) : IAzureAuthFileLock
    {
        public int Attempts { get; private set; }

        public void Lock(FileStream stream, long position, long length)
        {
            if (!OperatingSystem.IsLinux())
            {
                throw new PlatformNotSupportedException();
            }

            Attempts++;
            if (Attempts <= failuresBeforeSuccess)
            {
                throw new IOException("Injected lock contention.");
            }

            stream.Lock(position, length);
        }
    }

    private sealed class CoordinatedFileLock : IAzureAuthFileLock
    {
        private int attempts;

        public int Attempts => Volatile.Read(ref attempts);

        public ManualResetEventSlim FirstLockAcquired { get; } = new(false);

        public ManualResetEventSlim ReleaseFirstLock { get; } = new(false);

        public void Lock(FileStream stream, long position, long length)
        {
            if (!OperatingSystem.IsLinux())
            {
                throw new PlatformNotSupportedException();
            }

            if (Interlocked.Increment(ref attempts) == 1)
            {
                stream.Lock(position, length);
                FirstLockAcquired.Set();
                ReleaseFirstLock.Wait(TestContext.Current.CancellationToken);
                return;
            }

            stream.Lock(position, length);
        }
    }

    private sealed class ForeignOwnerMetadataProvider(string foreignOwnedPath)
        : ILinuxFileMetadataProvider
    {
        private readonly SystemLinuxFileMetadataProvider inner = OperatingSystem.IsLinux()
            ? new SystemLinuxFileMetadataProvider()
            : throw new PlatformNotSupportedException();

        public uint EffectiveUserId => inner.EffectiveUserId;

        public LinuxFileMetadata GetMetadataWithoutFollowingLinks(string path)
        {
            if (!OperatingSystem.IsLinux())
            {
                throw new PlatformNotSupportedException();
            }

            LinuxFileMetadata metadata = inner.GetMetadataWithoutFollowingLinks(path);
            return string.Equals(path, foreignOwnedPath, StringComparison.Ordinal)
                ? metadata with { UserId = EffectiveUserId + 1 }
                : metadata;
        }
    }
}
