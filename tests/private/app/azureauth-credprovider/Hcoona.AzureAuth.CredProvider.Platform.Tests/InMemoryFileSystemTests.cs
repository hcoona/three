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
    public void AtomicWriteAllTextReplacesSymbolicLinkPathWithoutChangingTarget()
    {
        var fileSystem = new InMemoryFileSystem();
        fileSystem.CreateDirectory("root");
        fileSystem.WriteAllText("root/target.txt", "target contents");
        fileSystem.SetUnixFileMode(
            "root/target.txt",
            UnixFileMode.UserRead | UnixFileMode.UserExecute
        );
        fileSystem.AddSymbolicLink("root/link.txt", "root/target.txt");

        fileSystem.AtomicWriteAllText("root/link.txt", "replacement contents");

        Assert.Equal("replacement contents", fileSystem.ReadAllText("root/link.txt"));
        Assert.Equal("target contents", fileSystem.ReadAllText("root/target.txt"));
        Assert.False(fileSystem.IsSymbolicLink("root/link.txt"));
        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserExecute,
            fileSystem.GetUnixFileMode("root/link.txt")
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

        fileSystem.DeleteFile("root/target-link/helper.txt");

        Assert.False(fileSystem.FileExists("root/target/helper.txt"));
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
