using System.Text;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class SystemFileSystemTests
{
    private static readonly Lazy<bool> DirectorySymbolicLinkCapability =
        new(ProbeDirectorySymbolicLinkCapability);
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false, true);

    [Fact]
    public void NormalOperationsReadWriteEnumerateAndDelete()
    {
        string root = CreateTestDirectory();
        string nested = Path.Combine(root, "nested");
        string textFile = Path.Combine(nested, "value.txt");
        string binaryFile = Path.Combine(nested, "value.bin");
        var fileSystem = new SystemFileSystem();

        try
        {
            fileSystem.CreateDirectory(nested);
            fileSystem.WriteAllText(textFile, "value");
            fileSystem.AtomicWriteAllBytes(binaryFile, [0, 1, 255]);

            Assert.True(fileSystem.FileExists(textFile));
            Assert.True(fileSystem.DirectoryExists(nested));
            Assert.True(fileSystem.IsPathFullyQualified(fileSystem.GetFullPath(textFile)));
            Assert.Equal("value", fileSystem.ReadAllText(textFile));
            Assert.Equal([0, 1, 255], fileSystem.ReadAllBytes(binaryFile));
            Assert.Equal(5, fileSystem.GetFileLength(textFile));
            Assert.Equal(
                [textFile],
                fileSystem.EnumerateFiles(root, "*.txt", SearchOption.AllDirectories)
            );
            Assert.Equal([nested], fileSystem.EnumerateDirectories(root));

            fileSystem.DeleteFile(textFile);
            fileSystem.DeleteDirectory(nested, recursive: true);

            Assert.False(fileSystem.FileExists(textFile));
            Assert.False(fileSystem.DirectoryExists(nested));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void TextWritesUseUtf8WithoutBomByDefault()
    {
        string root = CreateTestDirectory();
        string directPath = Path.Combine(root, "direct.txt");
        string atomicPath = Path.Combine(root, "atomic.txt");
        var fileSystem = new SystemFileSystem();

        try
        {
            fileSystem.WriteAllText(directPath, "héllo");
            fileSystem.AtomicWriteAllText(atomicPath, "héllo");

            byte[] expected = Utf8NoBom.GetBytes("héllo");
            Assert.Equal(expected, File.ReadAllBytes(directPath));
            Assert.Equal(expected, File.ReadAllBytes(atomicPath));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact(Skip = "Unix file mode test.", SkipWhen = nameof(IsWindows))]
    public void ExecutableFileCheckUsesUnixExecuteModes()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string root = CreateTestDirectory();
        string path = Path.Combine(root, "tool");
        var fileSystem = new SystemFileSystem();

        try
        {
            File.WriteAllText(path, "contents");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            Assert.False(fileSystem.IsExecutableFile(path));

            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
            );

            Assert.True(fileSystem.IsExecutableFile(path));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void AtomicWriteCreatesParentsAndReplacesExistingFile()
    {
        string root = CreateTestDirectory();
        string path = Path.Combine(root, "missing", "nested", "value.txt");
        var fileSystem = new SystemFileSystem();

        try
        {
            fileSystem.AtomicWriteAllText(path, "first");
            fileSystem.AtomicWriteAllText(path, "second");

            Assert.Equal("second", File.ReadAllText(path));
            Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void AtomicWriteFailureLeavesExistingFileAndRemovesTemporaryFile()
    {
        string root = CreateTestDirectory();
        string path = Path.Combine(root, "value.txt");
        var fileSystem = new SystemFileSystem();
        File.WriteAllText(path, "before");

        try
        {
            Assert.Throws<EncoderFallbackException>(() =>
                fileSystem.AtomicWriteAllText(path, "\ud800", Utf8NoBom)
            );

            Assert.Equal("before", File.ReadAllText(path));
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact(Skip = "Unix file mode test.", SkipWhen = nameof(IsWindows))]
    public void OwnerOnlyAtomicWriteAppliesRequestedUnixMode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string root = CreateTestDirectory();
        string path = Path.Combine(root, "secret.txt");
        var fileSystem = new SystemFileSystem();

        try
        {
            fileSystem.AtomicWriteAllText(
                path,
                "secret",
                options: AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly
            );

            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                fileSystem.GetUnixFileMode(path)
            );
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact(Skip = "Unix file mode test.", SkipWhen = nameof(IsWindows))]
    public void OwnerOnlyAtomicWriteCreatesTemporaryFileWithOwnerOnlyModeBeforeWriting()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string root = CreateTestDirectory();
        string path = Path.Combine(root, "secret.txt");
        string? observedTemporaryPath = null;
        UnixFileMode? observedMode = null;
        long? observedLength = null;
        SystemFileSystem? fileSystem = null;
        fileSystem = new SystemFileSystem(temporaryPath =>
        {
            observedTemporaryPath = temporaryPath;
            observedMode = fileSystem!.GetUnixFileMode(temporaryPath);
            observedLength = new FileInfo(temporaryPath).Length;
        });

        try
        {
            fileSystem.AtomicWriteAllText(
                path,
                "secret",
                options: AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly
            );

            UnixFileMode ownerOnlyMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            Assert.NotNull(observedTemporaryPath);
            Assert.Equal(ownerOnlyMode, observedMode);
            Assert.Equal(0, observedLength);
            Assert.Equal("secret", File.ReadAllText(path));
            Assert.Equal(ownerOnlyMode, File.GetUnixFileMode(path));
            Assert.Empty(Directory.EnumerateFiles(root, "*.tmp"));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact(Skip = "Unix file mode test.", SkipWhen = nameof(IsWindows))]
    public void AtomicWritePreservesExistingUnixMode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        string root = CreateTestDirectory();
        string path = Path.Combine(root, "configuration.txt");
        var fileSystem = new SystemFileSystem();
        UnixFileMode expectedMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        try
        {
            File.WriteAllText(path, "before");
            File.SetUnixFileMode(path, expectedMode);

            fileSystem.AtomicWriteAllText(path, "after");

            Assert.Equal("after", File.ReadAllText(path));
            Assert.Equal(expectedMode, File.GetUnixFileMode(path));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void ResolveFilePathForWriteResolvesExistingParentDirectoryLink()
    {
        string root = CreateTestDirectory();
        string physicalParent = Path.Combine(root, "physical", "config");
        string linkedParent = Path.Combine(root, "linked");
        Directory.CreateDirectory(physicalParent);
        if (!TryCreateDirectorySymbolicLink(linkedParent, Path.Combine(root, "physical")))
        {
            DeleteDirectoryIfExists(root);
            return;
        }

        try
        {
            string resolved = ((IFileSystemLinkResolver)new SystemFileSystem())
                .ResolveFilePathForWrite(
                    Path.Combine(linkedParent, "config", ".yarnrc.yml")
                );

            Assert.Equal(Path.Combine(physicalParent, ".yarnrc.yml"), resolved);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact(
        Skip = "Directory symbolic-link creation is unavailable.",
        SkipUnless = nameof(CanCreateDirectorySymbolicLinks)
    )]
    public void ResolveFilePathForWriteFullyCanonicalizesChainedAncestorLinksAndKeepsMissingFileName()
    {
        string root = CreateTestDirectory();
        string physical = Path.Combine(root, "physical");
        string physicalConfig = Path.Combine(physical, "deep", "config");
        string innerLink = Path.Combine(root, "inner-link");
        string outerLink = Path.Combine(root, "outer-link");
        const string MissingFileName = "not-created.yarnrc.yml";

        try
        {
            Directory.CreateDirectory(physicalConfig);
            Directory.CreateSymbolicLink(innerLink, physical);
            Directory.CreateSymbolicLink(outerLink, Path.Combine(innerLink, "deep"));

            DirectoryInfo intermediate = Assert.IsType<DirectoryInfo>(
                new DirectoryInfo(outerLink).ResolveLinkTarget(returnFinalTarget: false)
            );
            Assert.Equal(Path.Combine(innerLink, "deep"), intermediate.FullName);

            string resolved = ((IFileSystemLinkResolver)new SystemFileSystem())
                .ResolveFilePathForWrite(
                    Path.Combine(outerLink, "config", MissingFileName)
                );

            Assert.Equal(Path.Combine(physicalConfig, MissingFileName), resolved);
            Assert.Equal(MissingFileName, Path.GetFileName(resolved));
            Assert.False(File.Exists(resolved));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact(
        Skip = "Directory symbolic-link creation is unavailable.",
        SkipUnless = nameof(CanCreateDirectorySymbolicLinks)
    )]
    public void ResolveFilePathForWriteAllowsValidLinkReentryWithDifferentRemainingSuffix()
    {
        string root = CreateTestDirectory();
        string physical = Path.Combine(root, "physical");
        string link = Path.Combine(root, "link");
        string reentry = Path.Combine(physical, "reentry");
        string finalDirectory = Path.Combine(physical, "final");
        const string MissingFileName = "not-created.yml";

        try
        {
            Directory.CreateDirectory(finalDirectory);
            Directory.CreateSymbolicLink(link, physical);
            Directory.CreateSymbolicLink(reentry, link);

            string resolved = ((IFileSystemLinkResolver)new SystemFileSystem())
                .ResolveFilePathForWrite(
                    Path.Combine(link, "reentry", "final", MissingFileName)
                );

            Assert.Equal(Path.Combine(finalDirectory, MissingFileName), resolved);
            Assert.False(File.Exists(resolved));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact(
        Skip = "Directory symbolic-link creation is unavailable.",
        SkipUnless = nameof(CanCreateDirectorySymbolicLinks)
    )]
    public void ResolveFilePathForWriteRejectsAncestorLinkCycle()
    {
        string root = CreateTestDirectory();
        string firstLink = Path.Combine(root, "first-link");
        string secondLink = Path.Combine(root, "second-link");
        string requestedPath = Path.Combine(firstLink, "not-created.yml");

        try
        {
            Directory.CreateSymbolicLink(firstLink, secondLink);
            Directory.CreateSymbolicLink(secondLink, firstLink);

            IOException exception = Assert.Throws<IOException>(() =>
                ((IFileSystemLinkResolver)new SystemFileSystem())
                    .ResolveFilePathForWrite(requestedPath)
            );

            Assert.False(string.IsNullOrWhiteSpace(exception.Message));
            Assert.False(File.Exists(requestedPath));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact(
        Skip = "Directory symbolic-link creation is unavailable.",
        SkipUnless = nameof(CanCreateDirectorySymbolicLinks)
    )]
    public void ResolveFilePathForWriteRejectsRepeatedNormalizedTraversalState()
    {
        string root = CreateTestDirectory();
        string physical = Path.Combine(root, "physical");
        string link = Path.Combine(root, "link");
        string reentry = Path.Combine(physical, "reentry");
        string requestedPath = Path.Combine(link, "reentry", "not-created.yml");

        try
        {
            Directory.CreateDirectory(physical);
            Directory.CreateSymbolicLink(link, physical);
            Directory.CreateSymbolicLink(reentry, Path.Combine(link, "reentry"));

            IOException exception = Assert.Throws<IOException>(() =>
                ((IFileSystemLinkResolver)new SystemFileSystem())
                    .ResolveFilePathForWrite(requestedPath)
            );

            Assert.False(string.IsNullOrWhiteSpace(exception.Message));
            Assert.False(File.Exists(requestedPath));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact(
        Skip = "Windows directory-link creation is unavailable.",
        SkipUnless = nameof(CanCreateWindowsDirectoryLinks)
    )]
    public void ResolveFilePathForWriteCanonicalizesWindowsDirectoryLink()
    {
        string root = CreateTestDirectory();
        string physical = Path.Combine(root, "physical", "config");
        string link = Path.Combine(root, "linked-config");
        const string MissingFileName = "not-created.yml";

        try
        {
            Directory.CreateDirectory(physical);
            Directory.CreateSymbolicLink(link, physical);

            string resolved = ((IFileSystemLinkResolver)new SystemFileSystem())
                .ResolveFilePathForWrite(Path.Combine(link, MissingFileName));

            Assert.Equal(Path.Combine(physical, MissingFileName), resolved);
            Assert.False(File.Exists(resolved));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    [Fact]
    public void MutationLockSerializesCooperativeUsers()
    {
        string root = CreateTestDirectory();
        string lockDirectory = Path.Combine(root, "locks", "configuration");
        var fileSystem = new SystemFileSystem();
        var mutationLock = (IFileSystemMutationLock)fileSystem;

        try
        {
            using IDisposable first = mutationLock.AcquireMutationLock(lockDirectory);
            Assert.Throws<IOException>(() => mutationLock.AcquireMutationLock(lockDirectory));
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }

    public static bool CanCreateDirectorySymbolicLinks =>
        DirectorySymbolicLinkCapability.Value;

    public static bool CanCreateWindowsDirectoryLinks =>
        OperatingSystem.IsWindows() && CanCreateDirectorySymbolicLinks;

    public static bool IsWindows => OperatingSystem.IsWindows();

    private static string CreateTestDirectory()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "azureauth-credprovider-filesystem-tests",
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static bool TryCreateDirectorySymbolicLink(string path, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(path, targetPath);
            return true;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (IOException) when (OperatingSystem.IsWindows())
        {
            return false;
        }
    }

    private static bool ProbeDirectorySymbolicLinkCapability()
    {
        string root = CreateTestDirectory();
        string target = Path.Combine(root, "target");
        string link = Path.Combine(root, "link");
        try
        {
            Directory.CreateDirectory(target);
            return TryCreateDirectorySymbolicLink(link, target);
        }
        finally
        {
            DeleteDirectoryIfExists(root);
        }
    }
}
