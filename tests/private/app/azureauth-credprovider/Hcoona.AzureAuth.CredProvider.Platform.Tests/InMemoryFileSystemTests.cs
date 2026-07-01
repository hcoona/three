using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class InMemoryFileSystemTests
{
    private const UnixFileMode HelperExecutableMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    [Fact]
    public void FileSystemOperationsReadWriteDeleteEnumerateAndAtomicReplaceFiles()
    {
        var fileSystem = new InMemoryFileSystem();

        fileSystem.CreateDirectory("root/nested directory");
        fileSystem.WriteAllText("root/nested directory/file with spaces.txt", "first");
        fileSystem.WriteAllText("root/nested directory/atomic.txt", "old");
        fileSystem.AtomicWriteAllText("root/nested directory/atomic.txt", "new");

        Assert.True(fileSystem.DirectoryExists("root/nested directory"));
        Assert.True(fileSystem.FileExists("root/nested directory/file with spaces.txt"));
        Assert.Equal("first", fileSystem.ReadAllText("root/nested directory/file with spaces.txt"));
        Assert.Equal("new", fileSystem.ReadAllText("root/nested directory/atomic.txt"));
        Assert.Equal(
            [
                NormalizePath("root/nested directory/atomic.txt"),
                NormalizePath("root/nested directory/file with spaces.txt"),
            ],
            fileSystem.EnumerateFiles("root", "*.txt", SearchOption.AllDirectories).ToArray()
        );
        Assert.Equal(
            [NormalizePath("root/nested directory")],
            fileSystem.EnumerateDirectories("root").ToArray()
        );

        fileSystem.DeleteFile("root/nested directory/file with spaces.txt");
        Assert.False(fileSystem.FileExists("root/nested directory/file with spaces.txt"));

        fileSystem.DeleteDirectory("root/nested directory", recursive: true);
        Assert.False(fileSystem.DirectoryExists("root/nested directory"));
    }

    [Fact]
    public void OperationsRecordCallsWithNormalizedPaths()
    {
        var fileSystem = new InMemoryFileSystem();

        fileSystem.CreateDirectory("root/child");
        fileSystem.WriteAllText("root/child/file.txt", "contents");
        _ = fileSystem.ReadAllText("root/child/file.txt");

        Assert.Equal(
            [
                new FileSystemCall("CreateDirectory", NormalizePath("root/child")),
                new FileSystemCall(
                    "WriteAllText",
                    NormalizePath("root/child/file.txt"),
                    "contents"
                ),
                new FileSystemCall("ReadAllText", NormalizePath("root/child/file.txt")),
            ],
            fileSystem.Calls
        );
    }

    [Fact]
    public void AtomicWriteAllTextCreatesMissingParentDirectories()
    {
        var fileSystem = new InMemoryFileSystem();

        fileSystem.AtomicWriteAllText("root/created/atomic.txt", "created atomically");

        Assert.True(fileSystem.DirectoryExists("root"));
        Assert.True(fileSystem.DirectoryExists("root/created"));
        Assert.Equal("created atomically", fileSystem.ReadAllText("root/created/atomic.txt"));
    }

    [Fact]
    public void GetFileLengthReturnsRegularFileMetadataLength()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.CreateDirectory("/root");
        fileSystem.WriteAllText("/root/empty.lock", string.Empty);
        fileSystem.WriteAllText("/root/non-empty.lock", "abc");

        Assert.Equal(0, fileSystem.GetFileLength("/root/empty.lock"));
        Assert.Equal(3, fileSystem.GetFileLength("/root/non-empty.lock"));
    }

    [Fact]
    public void GetFileLengthRejectsFinalSymbolicLinkOrReparsePoint()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.CreateDirectory("/root");
        fileSystem.WriteAllText("/root/target.lock", string.Empty);
        fileSystem.WriteAllText("/root/reparse.lock", string.Empty);
        fileSystem.AddSymbolicLink("/root/link.lock", "/root/target.lock");
        fileSystem.MarkAsNonSymbolicReparsePoint("/root/reparse.lock");

        Assert.Throws<IOException>(() => fileSystem.GetFileLength("/root/link.lock"));
        Assert.Throws<IOException>(() => fileSystem.GetFileLength("/root/reparse.lock"));
    }

    [Fact]
    public void GetFileLengthRejectsDirectory()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.CreateDirectory("/root/directory.lock");

        Assert.Throws<IOException>(() => fileSystem.GetFileLength("/root/directory.lock"));
    }

    [Fact]
    public void ConditionalAtomicWriteExistingMissingTargetLeavesNoPersistentParents()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.CreateDirectory("/root");
        var expectation = FileMutationExpectation.Existing(
            Convert.ToHexString(Sha256("missing")).ToLowerInvariant()
        );

        Assert.Throws<InvalidOperationException>(() =>
            fileSystem.AtomicWriteAllText(
                "/root/created/nested/atomic.txt",
                "created atomically",
                expectation: expectation
            )
        );

        Assert.False(fileSystem.DirectoryExists("/root/created"));
        Assert.False(fileSystem.DirectoryExists("/root/created/nested"));
        Assert.False(fileSystem.FileExists("/root/created/nested/atomic.txt"));
    }

    [Fact]
    public void AtomicWriteAllTextRejectsSymbolicLinkParentBeforeCreatingEscapedParent()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.CreateDirectory("/root/config");
        fileSystem.CreateDirectory("/root/outside");
        fileSystem.AddSymbolicLink("/root/config/link", "/root/outside");

        var exception = Assert.Throws<NotSupportedException>(() =>
            fileSystem.AtomicWriteAllText("/root/config/link/nested/escape.txt", "secret")
        );

        Assert.Contains("symbolic-link", exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.DirectoryExists("/root/outside/nested"));
        Assert.False(fileSystem.FileExists("/root/outside/nested/escape.txt"));
    }

    [Fact]
    public void AddSymbolicLinkRejectsExistingSymbolicLinkPathWithoutOverwriting()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.CreateDirectory("/root");
        fileSystem.WriteAllText("/root/first-target.txt", "first target");
        fileSystem.WriteAllText("/root/second-target.txt", "second target");
        fileSystem.AddSymbolicLink("/root/link.txt", "/root/first-target.txt");

        var exception = Assert.Throws<IOException>(() =>
            fileSystem.AddSymbolicLink("/root/link.txt", "/root/second-target.txt")
        );

        Assert.Contains("already exists", exception.Message, StringComparison.Ordinal);
        Assert.True(fileSystem.IsSymbolicLink("/root/link.txt"));
        Assert.Equal("first target", fileSystem.ReadAllText("/root/link.txt"));
    }

    [Fact]
    public void AddSymbolicLinkRejectsExistingRegularFilePathWithoutOverwriting()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.CreateDirectory("/root");
        fileSystem.WriteAllText("/root/existing.txt", "existing contents");
        fileSystem.WriteAllText("/root/target.txt", "target contents");

        var exception = Assert.Throws<IOException>(() =>
            fileSystem.AddSymbolicLink("/root/existing.txt", "/root/target.txt")
        );

        Assert.Contains("already exists", exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.IsSymbolicLink("/root/existing.txt"));
        Assert.Equal("existing contents", fileSystem.ReadAllText("/root/existing.txt"));
    }

    [Fact]
    public void AddSymbolicLinkRejectsExistingDirectoryPathWithoutOverwriting()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.CreateDirectory("/root/existing");
        fileSystem.WriteAllText("/root/existing/child.txt", "child contents");
        fileSystem.WriteAllText("/root/target.txt", "target contents");

        var exception = Assert.Throws<IOException>(() =>
            fileSystem.AddSymbolicLink("/root/existing", "/root/target.txt")
        );

        Assert.Contains("already exists", exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.IsSymbolicLink("/root/existing"));
        Assert.True(fileSystem.DirectoryExists("/root/existing"));
        Assert.Equal("child contents", fileSystem.ReadAllText("/root/existing/child.txt"));
    }

    [Fact]
    public void AddSymbolicLinkRejectsExistingReparsePointPathWithoutOverwriting()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.CreateDirectory("/root");
        fileSystem.WriteAllText("/root/reparse.txt", "reparse contents");
        fileSystem.WriteAllText("/root/target.txt", "target contents");
        fileSystem.MarkAsNonSymbolicReparsePoint("/root/reparse.txt");

        var exception = Assert.Throws<IOException>(() =>
            fileSystem.AddSymbolicLink("/root/reparse.txt", "/root/target.txt")
        );

        Assert.Contains("already exists", exception.Message, StringComparison.Ordinal);
        Assert.False(fileSystem.IsSymbolicLink("/root/reparse.txt"));
        Assert.Equal("reparse contents", fileSystem.ReadAllText("/root/reparse.txt"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MutationLockRejectsFinalDirectorySymbolicLink(bool createDirectory)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string lockDirectory = "/root/config-lock";
        fileSystem.CreateDirectory("/root");
        fileSystem.CreateDirectory("/outside");
        fileSystem.AddSymbolicLink(lockDirectory, "/outside");

        var exception = Assert.Throws<IOException>(() =>
            ((IFileSystemMutationLock)fileSystem)
                .AcquireMutationLock(lockDirectory, createDirectory)
                .Dispose()
        );

        Assert.Contains("symbolic link", exception.Message, StringComparison.Ordinal);
        Assert.True(fileSystem.IsSymbolicLink(lockDirectory));
        Assert.False(fileSystem.Directories.Contains(lockDirectory));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MutationLockRejectsFinalNonSymbolicReparsePoint(bool createDirectory)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string lockDirectory = "/root/config-lock";
        fileSystem.CreateDirectory(lockDirectory);
        fileSystem.MarkAsNonSymbolicReparsePoint(lockDirectory);

        var exception = Assert.Throws<IOException>(() =>
            ((IFileSystemMutationLock)fileSystem)
                .AcquireMutationLock(lockDirectory, createDirectory)
                .Dispose()
        );

        Assert.Contains("reparse point", exception.Message, StringComparison.Ordinal);
        Assert.True(fileSystem.DirectoryExists(lockDirectory));
        Assert.False(fileSystem.IsSymbolicLink(lockDirectory));
    }

    [Fact]
    public void AtomicWriteAllTextRestrictsCreatedParentDirectoriesForSecrets()
    {
        var fileSystem = new InMemoryFileSystem();

        fileSystem.AtomicWriteAllText(
            "root/created/atomic.txt",
            "created atomically",
            options: AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly
        );

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            fileSystem.GetUnixFileMode("root")
        );
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            fileSystem.GetUnixFileMode("root/created")
        );
    }

    [Fact]
    public void CreateDirectoryUsesConfiguredDefaultUnixModeForNewDirectories()
    {
        const UnixFileMode expectedMode =
            UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupRead
            | UnixFileMode.GroupWrite
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead
            | UnixFileMode.OtherWrite
            | UnixFileMode.OtherExecute;
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix)
        {
            DefaultCreateDirectoryMode = expectedMode,
        };

        fileSystem.CreateDirectory("/root/created/nested");

        Assert.Equal(expectedMode, fileSystem.GetUnixFileMode("/root"));
        Assert.Equal(expectedMode, fileSystem.GetUnixFileMode("/root/created"));
        Assert.Equal(expectedMode, fileSystem.GetUnixFileMode("/root/created/nested"));
    }

    [Fact]
    public void AtomicWriteAllTextCreatesNewFileWithOwnerOnlyMode()
    {
        var fileSystem = new InMemoryFileSystem();

        fileSystem.AtomicWriteAllText("root/created/atomic.txt", "created atomically");

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            fileSystem.GetUnixFileMode("root/created/atomic.txt")
        );
    }

    [Fact]
    public void WriteAllTextThrowsWhenDirectoryExistsAtPath()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root/existing");

        Assert.Throws<IOException>(() => fileSystem.WriteAllText("root/existing", "contents"));
        Assert.True(fileSystem.DirectoryExists("root/existing"));
        Assert.False(fileSystem.FileExists("root/existing"));
    }

    [Fact]
    public void DeleteFileThrowsWhenDirectoryExistsAtPath()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root/existing");

        Assert.Throws<IOException>(() => fileSystem.DeleteFile("root/existing"));
        Assert.True(fileSystem.DirectoryExists("root/existing"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConditionalDeleteFileThrowsWhenDirectoryExistsAtPath(bool expectExisting)
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root/existing");
        FileMutationExpectation expectation = expectExisting
            ? FileMutationExpectation.Existing(
                Convert.ToHexString(Sha256("unused")).ToLowerInvariant()
            )
            : FileMutationExpectation.Missing;

        var exception = Assert.Throws<InvalidOperationException>(() =>
            fileSystem.DeleteFile("root/existing", expectation)
        );

        Assert.Contains("Configuration conflict", exception.Message, StringComparison.Ordinal);
        Assert.True(fileSystem.DirectoryExists("root/existing"));
    }

    [Fact]
    public void AtomicWriteAllTextThrowsWhenDirectoryExistsAtPath()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root/existing");

        Assert.Throws<IOException>(() =>
            fileSystem.AtomicWriteAllText("root/existing", "contents")
        );
        Assert.True(fileSystem.DirectoryExists("root/existing"));
        Assert.False(fileSystem.FileExists("root/existing"));
    }

    [Fact]
    public void AtomicWriteAllBytesThrowsWhenDirectoryExistsAtPath()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root/existing");

        Assert.Throws<IOException>(() => fileSystem.AtomicWriteAllBytes("root/existing", [0x01]));
        Assert.True(fileSystem.DirectoryExists("root/existing"));
        Assert.False(fileSystem.FileExists("root/existing"));
    }

    [Fact]
    public void AtomicWriteAllTextRejectsFinalNonSymbolicReparsePoint()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/root/reparse-target.txt";
        fileSystem.CreateDirectory("/root");
        fileSystem.WriteAllText(targetPath, "before");
        fileSystem.MarkAsNonSymbolicReparsePoint(targetPath);

        var exception = Assert.Throws<IOException>(() =>
            fileSystem.AtomicWriteAllText(targetPath, "after")
        );

        Assert.Contains("reparse point", exception.Message, StringComparison.Ordinal);
        Assert.Equal("before", fileSystem.ReadAllText(targetPath));
    }

    [Theory]
    [InlineData(nameof(IFileSystem.AtomicWriteAllText))]
    [InlineData(nameof(IFileSystem.AtomicWriteAllBytes))]
    public void ConditionalAtomicWriteRejectsParentSymlinkBeforeFollowingTargetForHash(
        string methodName
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string externalPath = "/outside/target.txt";
        const string targetPath = "/link/target.txt";
        fileSystem.CreateDirectory("/outside");
        fileSystem.WriteAllText(externalPath, "external");
        fileSystem.AddSymbolicLink("/link", "/outside");
        var expectation = FileMutationExpectation.Existing(HashText("wrong-before-state"));

        var exception = Assert.Throws<NotSupportedException>(() =>
            InvokeAtomicWrite(fileSystem, methodName, targetPath, expectation)
        );

        Assert.Contains("symbolic-link", exception.Message, StringComparison.Ordinal);
        Assert.Equal("external", fileSystem.ReadAllText(externalPath));
    }

    [Theory]
    [InlineData(nameof(IFileSystem.AtomicWriteAllText))]
    [InlineData(nameof(IFileSystem.AtomicWriteAllBytes))]
    public void ConditionalAtomicWriteRejectsParentReparsePointBeforeReadingTargetHash(
        string methodName
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/root/target.txt";
        fileSystem.CreateDirectory("/root");
        fileSystem.WriteAllText(targetPath, "before");
        fileSystem.MarkAsNonSymbolicReparsePoint("/root");
        var expectation = FileMutationExpectation.Existing(HashText("wrong-before-state"));

        var exception = Assert.Throws<NotSupportedException>(() =>
            InvokeAtomicWrite(fileSystem, methodName, targetPath, expectation)
        );

        Assert.Contains("reparse-point", exception.Message, StringComparison.Ordinal);
        Assert.Equal("before", fileSystem.ReadAllText(targetPath));
    }

    [Fact]
    public void DeleteFileRejectsFinalNonSymbolicReparsePoint()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/root/reparse-target.txt";
        fileSystem.CreateDirectory("/root");
        fileSystem.WriteAllText(targetPath, "before");
        fileSystem.MarkAsNonSymbolicReparsePoint(targetPath);

        var exception = Assert.Throws<IOException>(() => fileSystem.DeleteFile(targetPath));

        Assert.Contains("reparse point", exception.Message, StringComparison.Ordinal);
        Assert.True(fileSystem.FileExists(targetPath));
        Assert.Equal("before", fileSystem.ReadAllText(targetPath));
    }

    [Fact]
    public void ConditionalDeleteRejectsParentSymlinkBeforeFollowingTargetForHash()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string externalPath = "/outside/target.txt";
        const string targetPath = "/link/target.txt";
        fileSystem.CreateDirectory("/outside");
        fileSystem.WriteAllText(externalPath, "external");
        fileSystem.AddSymbolicLink("/link", "/outside");
        var expectation = FileMutationExpectation.Existing(HashText("wrong-before-state"));

        var exception = Assert.Throws<NotSupportedException>(() =>
            fileSystem.DeleteFile(targetPath, expectation)
        );

        Assert.Contains("symbolic-link", exception.Message, StringComparison.Ordinal);
        Assert.Equal("external", fileSystem.ReadAllText(externalPath));
    }

    [Fact]
    public void ConditionalDeleteRejectsParentReparsePointBeforeReadingTargetHash()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        const string targetPath = "/root/target.txt";
        fileSystem.CreateDirectory("/root");
        fileSystem.WriteAllText(targetPath, "before");
        fileSystem.MarkAsNonSymbolicReparsePoint("/root");
        var expectation = FileMutationExpectation.Existing(HashText("wrong-before-state"));

        var exception = Assert.Throws<NotSupportedException>(() =>
            fileSystem.DeleteFile(targetPath, expectation)
        );

        Assert.Contains("reparse-point", exception.Message, StringComparison.Ordinal);
        Assert.Equal("before", fileSystem.ReadAllText(targetPath));
    }

    [Fact]
    public void CreateDirectoryThrowsWhenFileExistsAtPath()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.WriteAllText("root/existing", "contents");

        Assert.Throws<IOException>(() => fileSystem.CreateDirectory("root/existing"));
        Assert.True(fileSystem.FileExists("root/existing"));
        Assert.False(fileSystem.DirectoryExists("root/existing"));
    }

    [Fact]
    public void CreateDirectoryThrowsWhenParentComponentIsFile()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.WriteAllText("root/file", "contents");

        Assert.Throws<IOException>(() => fileSystem.CreateDirectory("root/file/child"));
        Assert.True(fileSystem.FileExists("root/file"));
        Assert.False(fileSystem.DirectoryExists("root/file"));
        Assert.False(fileSystem.DirectoryExists("root/file/child"));
    }

    [Fact]
    public void AtomicWriteAllTextPreservesExistingFileMode()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.AtomicWriteAllText("root/file.txt", "old");
        const UnixFileMode expectedMode = UnixFileMode.UserRead | UnixFileMode.UserExecute;
        fileSystem.SetUnixFileMode("root/file.txt", expectedMode);

        fileSystem.AtomicWriteAllText("root/file.txt", "new");

        Assert.Equal("new", fileSystem.ReadAllText("root/file.txt"));
        Assert.Equal(expectedMode, fileSystem.GetUnixFileMode("root/file.txt"));
    }

    [Fact]
    public void AtomicWriteAllTextCanRestrictExistingFileModeForSecrets()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.AtomicWriteAllText("root/file.txt", "old");
        fileSystem.SetUnixFileMode(
            "root/file.txt",
            UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.GroupRead
                | UnixFileMode.OtherRead
        );

        fileSystem.AtomicWriteAllText(
            "root/file.txt",
            "new",
            options: AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly
        );

        Assert.Equal("new", fileSystem.ReadAllText("root/file.txt"));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            fileSystem.GetUnixFileMode("root/file.txt")
        );
    }

    [Fact]
    public void AtomicWriteAllTextFailsClosedForFinalSymbolicLinkPath()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.WriteAllText("root/target.txt", "target contents");
        fileSystem.SetUnixFileMode(
            "root/target.txt",
            UnixFileMode.UserRead | UnixFileMode.UserExecute
        );
        fileSystem.AddSymbolicLink("root/link.txt", "root/target.txt");

        Assert.Throws<IOException>(() =>
            fileSystem.AtomicWriteAllText("root/link.txt", "replacement contents")
        );

        Assert.Equal("target contents", fileSystem.ReadAllText("root/link.txt"));
        Assert.Equal("target contents", fileSystem.ReadAllText("root/target.txt"));
        Assert.True(fileSystem.IsSymbolicLink("root/link.txt"));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserExecute,
            fileSystem.GetUnixFileMode("root/target.txt")
        );
    }

    [Fact]
    public void ReadAllBytesAndAtomicWriteAllBytesRoundTripNonUtf8Bytes()
    {
        var fileSystem = new InMemoryFileSystem();
        byte[] contents = [0x00, 0xff, 0xfe, 0x80, 0x41];

        fileSystem.AtomicWriteAllBytes("root/file.bin", contents);
        contents[0] = 0x7f;
        byte[] readContents = fileSystem.ReadAllBytes("root/file.bin");
        readContents[1] = 0x7e;

        Assert.Equal([0x00, 0xff, 0xfe, 0x80, 0x41], fileSystem.ReadAllBytes("root/file.bin"));
        Assert.Equal(
            System.Security.Cryptography.SHA256.HashData(
                new byte[] { 0x00, 0xff, 0xfe, 0x80, 0x41 }
            ),
            fileSystem.ComputeSha256Hash("root/file.bin")
        );
    }

    [Fact]
    public void UnixFileModeOperationsRecordCallsWithNormalizedPaths()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.WriteAllText("root/file.txt", "contents");
        const UnixFileMode expectedMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        fileSystem.SetUnixFileMode("root/file.txt", expectedMode);

        Assert.Equal(expectedMode, fileSystem.GetUnixFileMode("root/file.txt"));
        Assert.Contains(
            new FileSystemCall(
                "SetUnixFileMode",
                NormalizePath("root/file.txt"),
                expectedMode.ToString()
            ),
            fileSystem.Calls
        );
        Assert.Contains(
            new FileSystemCall("GetUnixFileMode", NormalizePath("root/file.txt")),
            fileSystem.Calls
        );
    }

    [Fact]
    public void FailureInjectionRecordsCallAndLeavesExistingFileUnchanged()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.WriteAllText("root/file.txt", "old");
        var expectedException = new IOException("injected");
        fileSystem.FailNextCall(expectedException);

        var exception = Assert.Throws<IOException>(() =>
            fileSystem.AtomicWriteAllText("root/file.txt", "new")
        );

        Assert.Same(expectedException, exception);
        Assert.Equal("old", fileSystem.ReadAllText("root/file.txt"));
        Assert.Contains(
            new FileSystemCall("AtomicWriteAllText", NormalizePath("root/file.txt"), "new"),
            fileSystem.Calls
        );
    }

    [Fact]
    public void AtomicWriteAllBytesFailureLeavesExistingNonUtf8BytesUnchanged()
    {
        var fileSystem = new InMemoryFileSystem();
        byte[] originalContents = [0x00, 0xff, 0xfe, 0x80, 0x41];
        byte[] replacementContents = [0x7f, 0x01, 0x02];
        fileSystem.CreateDirectory("root");
        fileSystem.AtomicWriteAllBytes("root/file.bin", originalContents);
        var expectedException = new IOException("injected");
        fileSystem.FailNextCall(expectedException);

        var exception = Assert.Throws<IOException>(() =>
            fileSystem.AtomicWriteAllBytes("root/file.bin", replacementContents)
        );

        Assert.Same(expectedException, exception);
        Assert.Equal(
            new byte[] { 0x00, 0xff, 0xfe, 0x80, 0x41 },
            fileSystem.ReadAllBytes("root/file.bin")
        );
        Assert.Contains(
            new FileSystemCall(
                "AtomicWriteAllBytes",
                NormalizePath("root/file.bin"),
                Convert.ToHexString(replacementContents)
            ),
            fileSystem.Calls
        );
    }

    [Fact]
    public void AbsolutePathsAndParentSegmentsResolveToSameEntry()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root/child");
        fileSystem.WriteAllText("root/child/file.txt", "contents");
        var absolutePath = Path.GetFullPath(
            Path.Combine("root", "child", "..", "child", "file.txt")
        );

        Assert.Equal("contents", fileSystem.ReadAllText(absolutePath));
        Assert.Contains(
            new FileSystemCall("ReadAllText", NormalizePath("root/child/file.txt")),
            fileSystem.Calls
        );
    }

    [Fact]
    public void ParentSegmentTraversalDoesNotRemainInOriginalEnumerationScope()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.CreateDirectory("outside");
        fileSystem.WriteAllText("root/../outside/file.txt", "outside");

        Assert.Empty(fileSystem.EnumerateFiles("root", "*.txt", SearchOption.AllDirectories));
        Assert.Equal(
            [NormalizePath("outside/file.txt")],
            fileSystem.EnumerateFiles("outside", "*.txt", SearchOption.AllDirectories).ToArray()
        );
    }

    [Fact]
    public void EnumerateFilesWithEmbeddedWildcardMatchesDirectoryEnumerateFilesPattern()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        var systemDirectory = CreateTestDirectory("embedded wildcard");
        var fileNames = new[] { "file.txt", "file-one.txt", "file-two.json", "other-file.txt" };

        foreach (var fileName in fileNames)
        {
            fileSystem.WriteAllText($"root/{fileName}", string.Empty);
            File.WriteAllText(Path.Combine(systemDirectory, fileName), string.Empty);
        }

        var expectedFileNames = Directory
            .EnumerateFiles(systemDirectory, "file*.txt")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actualFileNames = fileSystem
            .EnumerateFiles("root", "file*.txt")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedFileNames, actualFileNames);
    }

    [Fact]
    public void IntegrityBoundaryOperationsUseInMemoryState()
    {
        var fileSystem = new InMemoryFileSystem();
        var owner = new FileSystemOwner("fake:owner");
        fileSystem.CreateDirectory("root");
        fileSystem.WriteAllText("root/helper", "helper contents");
        fileSystem.SetOwner("root/helper", owner);
        fileSystem.AddSymbolicLink("root/helper-link", "root/helper");

        Assert.Equal(Path.GetFullPath("root/helper"), fileSystem.GetFullPath("root/helper"));
        Assert.Equal(
            Path.IsPathFullyQualified(Path.GetFullPath("root/helper")),
            fileSystem.IsPathFullyQualified(Path.GetFullPath("root/helper"))
        );
        Assert.Equal(Sha256("helper contents"), fileSystem.ComputeSha256Hash("root/helper"));
        Assert.Equal(Sha256("helper contents"), fileSystem.ComputeSha256Hash("root/helper-link"));
        Assert.Equal("helper contents", fileSystem.ReadAllText("root/helper-link"));
        Assert.False(fileSystem.IsSymbolicLink("root/helper"));
        Assert.True(fileSystem.IsSymbolicLink("root/helper-link"));
        Assert.True(fileSystem.FileExists("root/helper-link"));
        Assert.Equal(owner, fileSystem.GetOwner("root/helper"));
        Assert.Equal(owner, fileSystem.GetOwner("root/helper-link"));
        Assert.Equal(fileSystem.CurrentOwner, fileSystem.GetCurrentOwner());
    }

    [Fact]
    public void GetOwnerFollowsSymbolicLinkToTargetOwner()
    {
        var fileSystem = new InMemoryFileSystem();
        var targetOwner = new FileSystemOwner("fake:target");
        var linkOwner = new FileSystemOwner("fake:link");
        fileSystem.CreateDirectory("root");
        fileSystem.WriteAllText("root/helper", "helper contents");
        fileSystem.SetOwner("root/helper", targetOwner);
        fileSystem.AddSymbolicLink("root/helper-link", "root/helper");
        fileSystem.SetOwner("root/helper-link", linkOwner);

        Assert.Equal(targetOwner, fileSystem.GetOwner("root/helper-link"));
    }

    [Fact]
    public void EnumerateFilesAndDirectoriesIncludeSymbolicLinks()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.CreateDirectory("root/target");
        fileSystem.WriteAllText("root/target/helper.txt", "helper contents");
        fileSystem.AddSymbolicLink("root/helper-link.txt", "root/target/helper.txt");
        fileSystem.AddSymbolicLink("root/target-link", "root/target");

        Assert.Equal(
            [NormalizePath("root/helper-link.txt"), NormalizePath("root/target/helper.txt")],
            fileSystem.EnumerateFiles("root", "*.txt", SearchOption.AllDirectories).ToArray()
        );
        Assert.Equal(
            [NormalizePath("root/target"), NormalizePath("root/target-link")],
            fileSystem.EnumerateDirectories("root").ToArray()
        );
        Assert.Equal(
            [NormalizePath("root/target-link/helper.txt")],
            fileSystem.EnumerateFiles("root/target-link", "*.txt").ToArray()
        );
    }

    [Fact]
    public void OperationsResolveSymbolicLinkParentComponents()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.CreateDirectory("root/target");
        fileSystem.AddSymbolicLink("root/target-link", "root/target");

        fileSystem.WriteAllText("root/target-link/helper.txt", "helper contents");

        Assert.True(fileSystem.FileExists("root/target-link/helper.txt"));
        Assert.Equal("helper contents", fileSystem.ReadAllText("root/target/helper.txt"));
        Assert.Equal("helper contents", fileSystem.ReadAllText("root/target-link/helper.txt"));
        Assert.Equal(
            [NormalizePath("root/target-link/helper.txt")],
            fileSystem.EnumerateFiles("root/target-link", "*.txt").ToArray()
        );

        var exception = Assert.Throws<NotSupportedException>(() =>
            fileSystem.DeleteFile("root/target-link/helper.txt")
        );

        Assert.Contains("symbolic-link", exception.Message, StringComparison.Ordinal);
        Assert.True(fileSystem.FileExists("root/target/helper.txt"));
    }

    [Fact]
    public void IntegritySnapshotBindsOwnerHashAndFileIdentity()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.WriteAllText("root/helper", "helper contents");
        fileSystem.SetUnixFileMode("root/helper", HelperExecutableMode);

        var snapshot = fileSystem.CaptureFileIntegritySnapshot("root/helper");

        Assert.Equal(NormalizePath("root/helper"), snapshot.FullPath);
        Assert.Equal(fileSystem.CurrentOwner, snapshot.Owner);
        Assert.Equal(HelperExecutableMode, snapshot.UnixFileMode);
        Assert.Equal(Sha256("helper contents"), snapshot.Sha256Hash);
        Assert.Equal(NormalizePath("root"), snapshot.TrustedParentDirectories[0].FullPath);
        var mutableHashCopy = snapshot.Sha256Hash;
        mutableHashCopy[0] ^= 0xff;
        Assert.True(fileSystem.FileMatchesIntegritySnapshot("root/helper", snapshot));
        Assert.Equal(Sha256("helper contents"), snapshot.Sha256Hash);

        fileSystem.AtomicWriteAllText("root/helper", "replacement");

        Assert.False(fileSystem.FileMatchesIntegritySnapshot("root/helper", snapshot));
    }

    [Fact]
    public void WindowsIntegritySnapshotRevalidationIgnoresTrustedParentDirectoryPathCasing()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Windows);
        fileSystem.CreateDirectory(@"C:\Root\Nested");
        fileSystem.WriteAllText(@"C:\Root\Nested\File.txt", "helper contents");
        fileSystem.SetUnixFileMode(@"C:\Root\Nested\File.txt", HelperExecutableMode);

        var snapshot = fileSystem.CaptureFileIntegritySnapshot(@"C:\Root\Nested\File.txt");

        Assert.Equal(
            [@"C:\Root\Nested", @"C:\Root", @"C:\"],
            snapshot
                .TrustedParentDirectories.Select(static directory => directory.FullPath)
                .ToArray()
        );
        Assert.True(fileSystem.FileMatchesIntegritySnapshot(@"c:\root\nested\file.txt", snapshot));
    }

    [Fact]
    public void IntegritySnapshotRevalidationFailsWhenFileIdentityChanges()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.WriteAllText("root/helper", "helper contents");
        fileSystem.SetUnixFileMode("root/helper", HelperExecutableMode);

        var snapshot = fileSystem.CaptureFileIntegritySnapshot("root/helper");
        fileSystem.AtomicWriteAllText("root/helper", "helper contents");

        Assert.False(fileSystem.FileMatchesIntegritySnapshot("root/helper", snapshot));
    }

    [Fact]
    public void AtomicWriteAllTextAndCaptureSnapshotNoFollowRecordsDedicatedPostWriteOperation()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.CreateDirectory("/root");
        const string originalContents = "helper contents";
        const string replacementContents = "replacement contents";
        bool raceInjected = false;
        fileSystem.AfterRecord = (call, system) =>
        {
            if (
                raceInjected
                || call.Operation
                    != nameof(
                        InMemoryFileSystem.AtomicWriteAllTextAndCaptureSnapshotNoFollow
                    )
                || call.Path != "/root/helper"
            )
            {
                return;
            }

            raceInjected = true;
            system.DeleteFile("/root/helper");
            system.WriteAllText("/root/helper", replacementContents);
        };

        FileIntegritySnapshot snapshot = fileSystem.AtomicWriteAllTextAndCaptureSnapshotNoFollow(
            "/root/helper",
            originalContents
        );
        fileSystem.AfterRecord = null;

        Assert.True(raceInjected);
        Assert.Contains(
            fileSystem.Calls,
            call =>
                call.Operation
                    == nameof(InMemoryFileSystem.AtomicWriteAllTextAndCaptureSnapshotNoFollow)
                && call.Path == "/root/helper"
        );
        Assert.Equal("/root/helper", snapshot.FullPath);
        Assert.Equal(Sha256(originalContents), snapshot.Sha256Hash);
        Assert.Equal(replacementContents, fileSystem.ReadAllText("/root/helper"));
        Assert.False(fileSystem.FileMatchesIntegritySnapshot("/root/helper", snapshot));
    }

    [Fact]
    public void
    AtomicWriteAllTextAndCaptureSnapshotNoFollowRollsBackCreatedFileWhenPostWriteFailureIsInjected()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.CreateDirectory("/root");
        bool failureInjected = false;
        fileSystem.AfterRecord = (call, system) =>
        {
            if (
                failureInjected
                || call.Operation
                    != nameof(
                        InMemoryFileSystem.AtomicWriteAllTextAndCaptureSnapshotNoFollow
                    )
                || call.Path != "/root/helper"
            )
            {
                return;
            }

            failureInjected = true;
            system.FailNextCall(new IOException("post-write failure"));
        };

        IOException exception = Assert.Throws<IOException>(() =>
            fileSystem.AtomicWriteAllTextAndCaptureSnapshotNoFollow(
                "/root/helper",
                "helper contents"
            )
        );
        fileSystem.AfterRecord = null;

        Assert.Contains("post-write failure", exception.Message, StringComparison.Ordinal);
        Assert.True(failureInjected);
        Assert.False(fileSystem.FileExists("/root/helper"));
        Assert.True(fileSystem.DirectoryExists("/root"));
    }

    [Fact]
    public void IntegritySnapshotRevalidationFailsWhenAncestorDirectoryIsRecreated()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root/ancestor/parent");
        fileSystem.WriteAllText("root/ancestor/parent/helper", "helper contents");
        fileSystem.SetUnixFileMode("root/ancestor/parent/helper", HelperExecutableMode);

        var snapshot = fileSystem.CaptureFileIntegritySnapshot("root/ancestor/parent/helper");
        fileSystem.DeleteDirectory("root/ancestor", recursive: true);
        fileSystem.CreateDirectory("root/ancestor/parent");
        fileSystem.WriteAllText("root/ancestor/parent/helper", "helper contents");

        Assert.False(
            fileSystem.FileMatchesIntegritySnapshot("root/ancestor/parent/helper", snapshot)
        );
    }

    [Fact]
    public void IntegritySnapshotRejectsSymbolicLinkParentComponents()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.CreateDirectory("root/target");
        fileSystem.WriteAllText("root/target/helper", "helper contents");
        fileSystem.SetUnixFileMode("root/target/helper", HelperExecutableMode);
        fileSystem.AddSymbolicLink("root/target-link", "root/target");

        Assert.Throws<IOException>(() =>
            fileSystem.CaptureFileIntegritySnapshot("root/target-link/helper")
        );
    }

    [Fact]
    public void IntegritySnapshotRejectsDotOrDotDotComponentsBeforePathNormalization()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.CreateDirectory("/root");
        fileSystem.CreateDirectory("/root/target");
        fileSystem.WriteAllText("/root/helper", "helper contents");
        fileSystem.SetUnixFileMode("/root/helper", HelperExecutableMode);
        fileSystem.AddSymbolicLink("/root/target-link", "/root/target");
        var snapshot = fileSystem.CaptureFileIntegritySnapshot("/root/helper");

        Assert.Throws<IOException>(() => fileSystem.CaptureFileIntegritySnapshot("/root/./helper"));
        Assert.Throws<IOException>(() =>
            fileSystem.CaptureFileIntegritySnapshot("/root/target-link/../helper")
        );
        Assert.Throws<IOException>(() =>
            fileSystem.CaptureTrustedParentDirectorySnapshots("/root/target-link/../helper")
        );
        Assert.False(
            fileSystem.FileMatchesIntegritySnapshot("/root/target-link/../helper", snapshot)
        );
    }

    [Fact]
    public void IntegritySnapshotRejectsSymbolicLink()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.WriteAllText("root/helper", "helper contents");
        fileSystem.SetUnixFileMode("root/helper", HelperExecutableMode);
        fileSystem.AddSymbolicLink("root/helper-link", "root/helper");

        Assert.Throws<IOException>(() =>
            fileSystem.CaptureFileIntegritySnapshot("root/helper-link")
        );
    }

    [Fact]
    public void IntegritySnapshotRejectsNonSymbolicReparsePointFile()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.WriteAllText("root/helper", "helper contents");
        fileSystem.SetUnixFileMode("root/helper", HelperExecutableMode);
        fileSystem.MarkAsNonSymbolicReparsePoint("root/helper");

        Assert.Throws<IOException>(() =>
            fileSystem.CaptureFileIntegritySnapshot("root/helper")
        );
    }

    [Fact]
    public void IntegritySnapshotRejectsNonSymbolicReparsePointParentComponents()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.CreateDirectory("root/parent");
        fileSystem.WriteAllText("root/parent/helper", "helper contents");
        fileSystem.SetUnixFileMode("root/parent/helper", HelperExecutableMode);
        fileSystem.MarkAsNonSymbolicReparsePoint("root/parent");

        Assert.Throws<IOException>(() =>
            fileSystem.CaptureFileIntegritySnapshot("root/parent/helper")
        );
        Assert.Throws<IOException>(() =>
            fileSystem.CaptureTrustedParentDirectorySnapshots("root/parent/helper")
        );
    }

    [Theory]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupWrite)]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherWrite)]
    public void IntegritySnapshotRejectsGroupOrOtherWritableFile(UnixFileMode unsafeMode)
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.WriteAllText("root/helper", "helper contents");
        fileSystem.SetUnixFileMode("root/helper", unsafeMode);

        Assert.Throws<UnauthorizedAccessException>(() =>
            fileSystem.CaptureFileIntegritySnapshot("root/helper")
        );
    }

    [Fact]
    public void IntegritySnapshotRejectsNonExecutableFile()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.WriteAllText("root/helper", "helper contents");
        fileSystem.SetUnixFileMode("root/helper", UnixFileMode.UserRead | UnixFileMode.UserWrite);

        Assert.Throws<UnauthorizedAccessException>(() =>
            fileSystem.CaptureFileIntegritySnapshot("root/helper")
        );
    }

    [Theory]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupExecute)]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherExecute)]
    public void IntegritySnapshotRejectsCurrentUserOwnedFileWithoutUserExecute(
        UnixFileMode unsafeMode
    )
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.WriteAllText("root/helper", "helper contents");
        fileSystem.SetUnixFileMode("root/helper", unsafeMode);

        Assert.Throws<UnauthorizedAccessException>(() =>
            fileSystem.CaptureFileIntegritySnapshot("root/helper")
        );
    }

    [Theory]
    [InlineData("root/helper")]
    [InlineData("root")]
    public void IntegritySnapshotRejectsUntrustedOwner(string untrustedPath)
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.WriteAllText("root/helper", "helper contents");
        fileSystem.SetUnixFileMode("root/helper", HelperExecutableMode);
        fileSystem.SetOwner(untrustedPath, new FileSystemOwner("fake:attacker"));

        Assert.Throws<UnauthorizedAccessException>(() =>
            fileSystem.CaptureFileIntegritySnapshot("root/helper")
        );
    }

    [Theory]
    [InlineData(
        UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupWrite
    )]
    [InlineData(
        UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.OtherWrite
    )]
    public void IntegritySnapshotRejectsGroupOrOtherWritableParentDirectory(UnixFileMode unsafeMode)
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.WriteAllText("root/helper", "helper contents");
        fileSystem.SetUnixFileMode("root/helper", HelperExecutableMode);
        fileSystem.SetUnixFileMode("root", unsafeMode);

        Assert.Throws<UnauthorizedAccessException>(() =>
            fileSystem.CaptureFileIntegritySnapshot("root/helper")
        );
    }

    [Theory]
    [InlineData(
        UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupWrite
    )]
    [InlineData(
        UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute
            | UnixFileMode.OtherWrite
    )]
    public void IntegritySnapshotRejectsGroupOrOtherWritableAncestorDirectory(
        UnixFileMode unsafeMode
    )
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root/child");
        fileSystem.WriteAllText("root/child/helper", "helper contents");
        fileSystem.SetUnixFileMode("root/child/helper", HelperExecutableMode);
        fileSystem.SetUnixFileMode("root", unsafeMode);

        Assert.Throws<UnauthorizedAccessException>(() =>
            fileSystem.CaptureFileIntegritySnapshot("root/child/helper")
        );
    }

    [Fact]
    public void IntegritySnapshotRevalidationIncludesUnixFileMode()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.WriteAllText("root/helper", "helper contents");
        fileSystem.SetUnixFileMode("root/helper", HelperExecutableMode);

        var snapshot = fileSystem.CaptureFileIntegritySnapshot("root/helper");
        fileSystem.SetUnixFileMode("root/helper", UnixFileMode.UserRead);

        Assert.False(fileSystem.FileMatchesIntegritySnapshot("root/helper", snapshot));

        fileSystem.SetUnixFileMode("root/helper", UnixFileMode.UserRead | UnixFileMode.GroupWrite);

        Assert.False(fileSystem.FileMatchesIntegritySnapshot("root/helper", snapshot));
    }

    [Fact]
    public void IntegritySnapshotRevalidationFailsWhenParentDirectoryIsRecreated()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root/parent");
        fileSystem.WriteAllText("root/parent/helper", "helper contents");
        fileSystem.SetUnixFileMode("root/parent/helper", HelperExecutableMode);

        var snapshot = fileSystem.CaptureFileIntegritySnapshot("root/parent/helper");
        fileSystem.DeleteDirectory("root/parent", recursive: true);
        fileSystem.CreateDirectory("root/parent");
        fileSystem.WriteAllText("root/parent/helper", "helper contents");

        Assert.False(fileSystem.FileMatchesIntegritySnapshot("root/parent/helper", snapshot));
    }

    [Fact]
    public void IntegritySnapshotRevalidationIncludesTrustedParentDirectory()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.WriteAllText("root/helper", "helper contents");
        fileSystem.SetUnixFileMode("root/helper", HelperExecutableMode);

        var snapshot = fileSystem.CaptureFileIntegritySnapshot("root/helper");
        fileSystem.SetOwner("root", new FileSystemOwner("fake:replacement-owner"));

        Assert.False(fileSystem.FileMatchesIntegritySnapshot("root/helper", snapshot));

        fileSystem.SetOwner("root", fileSystem.CurrentOwner);
        fileSystem.SetUnixFileMode("root", UnixFileMode.UserRead);

        Assert.False(fileSystem.FileMatchesIntegritySnapshot("root/helper", snapshot));
    }

    [Fact]
    public void DeleteDirectoryRemovesDirectorySymlinkWithoutDeletingTarget()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.CreateDirectory("root/target");
        fileSystem.WriteAllText("root/target/helper", "helper contents");
        fileSystem.AddSymbolicLink("root/target-link", "root/target");

        fileSystem.DeleteDirectory("root/target-link");

        Assert.False(fileSystem.DirectoryExists("root/target-link"));
        Assert.Throws<FileNotFoundException>(() => fileSystem.IsSymbolicLink("root/target-link"));
        Assert.True(fileSystem.DirectoryExists("root/target"));
        Assert.Equal("helper contents", fileSystem.ReadAllText("root/target/helper"));
    }

    [Fact]
    public void RecursiveDirectoryDeleteRemovesNestedSymbolicLinks()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root/deleted");
        fileSystem.CreateDirectory("root/target");
        fileSystem.WriteAllText("root/target/helper", "helper contents");
        fileSystem.AddSymbolicLink("root/deleted/helper-link", "root/target/helper");

        fileSystem.DeleteDirectory("root/deleted", recursive: true);

        Assert.False(fileSystem.DirectoryExists("root/deleted"));
        Assert.False(fileSystem.FileExists("root/deleted/helper-link"));
        Assert.True(fileSystem.DirectoryExists("root/target"));
        Assert.Equal("helper contents", fileSystem.ReadAllText("root/target/helper"));
    }

    [Fact]
    public void PosixPathSemanticsUseSlashRootAndTreatBackslashAsFileNameCharacter()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.CreateDirectory("/root");
        fileSystem.WriteAllText("/root/name\\with\\backslashes.txt", "contents");

        Assert.Equal(
            "/root/name\\with\\backslashes.txt",
            fileSystem.GetFullPath("root/./name\\with\\backslashes.txt")
        );
        Assert.True(fileSystem.IsPathFullyQualified("/root"));
        Assert.False(fileSystem.IsPathFullyQualified("root"));
        Assert.False(fileSystem.DirectoryExists("/root/name"));
        Assert.Equal(
            ["/root/name\\with\\backslashes.txt"],
            fileSystem.EnumerateFiles("/root", "*.txt", SearchOption.AllDirectories).ToArray()
        );
    }

    [Fact]
    public void BackslashPathHandlingMatchesCurrentPlatform()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        var pathWithBackslashes = Path.Combine("root", "name\\with\\backslashes.txt");

        if (OperatingSystem.IsWindows())
        {
            fileSystem.CreateDirectory(Path.Combine("root", "name", "with"));
        }

        fileSystem.WriteAllText(pathWithBackslashes, "contents");

        Assert.Equal("contents", fileSystem.ReadAllText(pathWithBackslashes));
        Assert.Equal(
            [Path.GetFullPath(pathWithBackslashes)],
            fileSystem.EnumerateFiles("root", "*.txt", SearchOption.AllDirectories).ToArray()
        );
    }

    [Fact]
    public void WindowsPathSemanticsUseDriveRootsBackslashesAndCaseInsensitiveLookup()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Windows);

        fileSystem.CreateDirectory(@"C:\Root\Nested");
        fileSystem.WriteAllText(@"c:/root/nested/File.txt", "contents");

        Assert.True(fileSystem.IsPathFullyQualified(@"C:\Root\Nested\File.txt"));
        Assert.False(fileSystem.IsPathFullyQualified(@"Root\Nested\File.txt"));
        Assert.Equal(@"C:\Root\Nested\File.txt", fileSystem.GetFullPath(@"Root\Nested\File.txt"));
        Assert.True(fileSystem.DirectoryExists(@"c:\ROOT\nested"));
        Assert.True(fileSystem.FileExists(@"C:\ROOT\NESTED\file.txt"));
        Assert.Equal("contents", fileSystem.ReadAllText(@"C:\ROOT\nested\FILE.txt"));
        Assert.Equal(
            [@"C:\root\nested\File.txt"],
            fileSystem
                .EnumerateFiles(@"C:\ROOT", "*.txt", SearchOption.AllDirectories)
                .ToArray()
        );
    }

    [Theory]
    [InlineData(@"C:")]
    [InlineData(@"D:")]
    public void WindowsPathSemanticsRejectBareDriveRootsFailClosed(string path)
    {
        AssertWindowsUnsupportedDriveRelativeOrBareDrivePathFailsClosed(path);
    }

    [Theory]
    [InlineData(@"C:relative")]
    [InlineData(@"D:relative\root")]
    public void WindowsPathSemanticsRejectDriveRelativeRootsFailClosed(string path)
    {
        AssertWindowsUnsupportedDriveRelativeOrBareDrivePathFailsClosed(path);
    }

    [Theory]
    [InlineData(@"\\server\share\root\file.txt")]
    [InlineData(@"\\?\UNC\server\share\root\file.txt")]
    [InlineData(@"\??\UNC\server\share\root\file.txt")]
    [InlineData(@"\Global??\UNC\server\share\root\file.txt")]
    public void WindowsPathSemanticsRejectUnsupportedUncRootsFailClosed(string path)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Windows);

        NotSupportedException getFullPathException = Assert.Throws<NotSupportedException>(() =>
            fileSystem.GetFullPath(path)
        );
        NotSupportedException createDirectoryException = Assert.Throws<NotSupportedException>(() =>
            fileSystem.CreateDirectory(path)
        );

        Assert.Contains("UNC", getFullPathException.Message, StringComparison.Ordinal);
        Assert.Contains(
            "drive-qualified",
            createDirectoryException.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(fileSystem.Calls);
        Assert.Empty(fileSystem.Files);
        Assert.Equal(
            [@"C:\"],
            fileSystem
                .Directories.OrderBy(
                    static directory => directory,
                    StringComparer.OrdinalIgnoreCase
                )
                .ToArray()
        );
    }

    [Theory]
    [InlineData(@"\root\file.txt")]
    [InlineData(@"/root/file.txt")]
    [InlineData(@"\??\C:\root\file.txt")]
    [InlineData(@"\Global??\C:\root\file.txt")]
    public void WindowsPathSemanticsRejectUnsupportedRootedNonDriveOrDevicePathsFailClosed(
        string path
    )
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Windows);

        NotSupportedException getFullPathException = Assert.Throws<NotSupportedException>(() =>
            fileSystem.GetFullPath(path)
        );
        NotSupportedException createDirectoryException = Assert.Throws<NotSupportedException>(() =>
            fileSystem.CreateDirectory(path)
        );

        Assert.Contains(
            "non-drive-qualified",
            getFullPathException.Message,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "drive-qualified",
            createDirectoryException.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(fileSystem.Calls);
        Assert.Empty(fileSystem.Files);
        Assert.Equal(
            [@"C:\"],
            fileSystem
                .Directories.OrderBy(
                    static directory => directory,
                    StringComparer.OrdinalIgnoreCase
                )
                .ToArray()
        );
    }

    private static void AssertWindowsUnsupportedDriveRelativeOrBareDrivePathFailsClosed(string path)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Windows);

        NotSupportedException getFullPathException = Assert.Throws<NotSupportedException>(() =>
            fileSystem.GetFullPath(path)
        );
        NotSupportedException createDirectoryException = Assert.Throws<NotSupportedException>(() =>
            fileSystem.CreateDirectory(path)
        );

        Assert.Contains(
            "drive-relative or bare-drive roots",
            getFullPathException.Message,
            StringComparison.Ordinal
        );
        Assert.Contains(
            "drive-relative or bare-drive roots",
            createDirectoryException.Message,
            StringComparison.Ordinal
        );
        Assert.Empty(fileSystem.Calls);
        Assert.Empty(fileSystem.Files);
        Assert.Equal(
            [@"C:\"],
            fileSystem
                .Directories.OrderBy(
                    static directory => directory,
                    StringComparer.OrdinalIgnoreCase
                )
                .ToArray()
        );
    }

    private static string CreateTestDirectory(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, name, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static byte[] Sha256(string contents)
    {
        return System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(contents)
        );
    }

    private static string HashText(string contents) =>
        Convert.ToHexString(Sha256(contents)).ToLowerInvariant();

    private static void InvokeAtomicWrite(
        InMemoryFileSystem fileSystem,
        string methodName,
        string targetPath,
        FileMutationExpectation expectation
    )
    {
        switch (methodName)
        {
            case nameof(IFileSystem.AtomicWriteAllText):
                fileSystem.AtomicWriteAllText(targetPath, "after", expectation: expectation);
                break;

            case nameof(IFileSystem.AtomicWriteAllBytes):
                fileSystem.AtomicWriteAllBytes(
                    targetPath,
                    System.Text.Encoding.UTF8.GetBytes("after"),
                    expectation: expectation
                );
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(methodName), methodName, null);
        }
    }

    private static string NormalizePath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var trimmedPath = Path.TrimEndingDirectorySeparator(fullPath);
        if (trimmedPath.Length == 0)
        {
            trimmedPath = Path.GetPathRoot(fullPath) ?? fullPath;
        }

        return trimmedPath;
    }
}
