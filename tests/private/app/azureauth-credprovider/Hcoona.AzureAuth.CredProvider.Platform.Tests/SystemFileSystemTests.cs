using System.Runtime.Versioning;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class SystemFileSystemTests
{
    private const UnixFileMode HelperExecutableMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    [Fact]
    public void FileSystemOperationsReadWriteDeleteEnumerateAndAtomicReplaceFiles()
    {
        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var nested = Path.Combine(root, "nested directory");
        var file = Path.Combine(nested, "file with spaces.txt");
        var atomicFile = Path.Combine(nested, "atomic.txt");

        try
        {
            fileSystem.CreateDirectory(nested);
            fileSystem.WriteAllText(file, "first");
            fileSystem.WriteAllText(atomicFile, "old");

            fileSystem.AtomicWriteAllText(atomicFile, "new");

            Assert.True(fileSystem.DirectoryExists(nested));
            Assert.True(fileSystem.FileExists(file));
            Assert.Equal("first", fileSystem.ReadAllText(file));
            Assert.Equal("new", fileSystem.ReadAllText(atomicFile));
            Assert.Equal(
                [atomicFile, file],
                fileSystem
                    .EnumerateFiles(root, "*.txt", SearchOption.AllDirectories)
                    .Order()
                    .ToArray()
            );
            Assert.Equal([nested], fileSystem.EnumerateDirectories(root).ToArray());

            fileSystem.DeleteFile(file);
            Assert.False(fileSystem.FileExists(file));

            fileSystem.DeleteDirectory(nested, recursive: true);
            Assert.False(fileSystem.DirectoryExists(nested));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void AtomicWriteAllTextCreatesMissingParentDirectory()
    {
        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var file = Path.Combine(root, "created", "nested", "atomic.txt");

        try
        {
            fileSystem.AtomicWriteAllText(file, "created atomically");

            Assert.True(fileSystem.DirectoryExists(Path.GetDirectoryName(file)!));
            Assert.True(fileSystem.FileExists(file));
            Assert.Equal("created atomically", fileSystem.ReadAllText(file));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void AtomicWriteAllTextRestrictsCreatedUnixParentDirectoriesForSecrets()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var created = Path.Combine(root, "created");
        var nested = Path.Combine(created, "nested");
        var file = Path.Combine(nested, "secret.txt");
        const UnixFileMode expectedDirectoryMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

        try
        {
            fileSystem.AtomicWriteAllText(
                file,
                "created atomically",
                options: AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly
            );

            Assert.Equal("created atomically", fileSystem.ReadAllText(file));
            Assert.Equal(expectedDirectoryMode, fileSystem.GetUnixFileMode(created));
            Assert.Equal(expectedDirectoryMode, fileSystem.GetUnixFileMode(nested));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void AtomicWriteAllTextDoesNotAttemptUnsupportedWindowsDirectoryFlush()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var createdFile = Path.Combine(root, "created", "nested", "created.txt");
        var replacedFile = Path.Combine(root, "replaced.txt");

        try
        {
            fileSystem.WriteAllText(replacedFile, "old");

            // Windows cannot flush directory entries with FlushFileBuffers, so
            // atomic writes must succeed without attempting that POSIX boundary.
            fileSystem.AtomicWriteAllText(createdFile, "created");
            fileSystem.AtomicWriteAllText(replacedFile, "new");

            Assert.Equal("created", fileSystem.ReadAllText(createdFile));
            Assert.Equal("new", fileSystem.ReadAllText(replacedFile));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void AtomicWriteAllTextFailsClosedOnUnsupportedPlatformsBeforeCreatingParents()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = Path.Combine(
            AppContext.BaseDirectory,
            "filesystem tests",
            Guid.NewGuid().ToString("N")
        );
        var created = Path.Combine(root, "created");
        var file = Path.Combine(created, "nested", "atomic.txt");

        try
        {
            Directory.CreateDirectory(root);

            Assert.Throws<PlatformNotSupportedException>(() =>
                fileSystem.AtomicWriteAllText(file, "created")
            );
            Assert.False(Directory.Exists(created));
            Assert.False(File.Exists(file));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void AtomicWriteAllTextCreatesNewUnixFileWithOwnerOnlyMode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var file = Path.Combine(root, "created", "restricted.txt");
        const UnixFileMode expectedMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        try
        {
            fileSystem.AtomicWriteAllText(file, "created securely");

            Assert.Equal("created securely", fileSystem.ReadAllText(file));
            Assert.Equal(expectedMode, fileSystem.GetUnixFileMode(file));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void AtomicWriteAllTextPreservesUnixFileModeWhenReplacingExistingFile()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var file = Path.Combine(root, "restricted.txt");
        const UnixFileMode expectedMode = UnixFileMode.UserRead | UnixFileMode.UserExecute;

        try
        {
            File.WriteAllText(file, "old");
            fileSystem.SetUnixFileMode(file, expectedMode);

            fileSystem.AtomicWriteAllText(file, "new");

            Assert.Equal("new", fileSystem.ReadAllText(file));
            Assert.Equal(expectedMode, fileSystem.GetUnixFileMode(file));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void UnixFileModeOperationsReadAndUpdateFileMode()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var file = Path.Combine(root, "restricted.txt");
        const UnixFileMode expectedMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        try
        {
            fileSystem.WriteAllText(file, "contents");
            fileSystem.SetUnixFileMode(file, expectedMode);

            Assert.Equal(expectedMode, fileSystem.GetUnixFileMode(file));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void IntegrityBoundaryOperationsReadSystemState()
    {
        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var file = Path.Combine(root, "helper");

        try
        {
            fileSystem.WriteAllText(file, "helper contents");

            Assert.Equal(Path.GetFullPath(file), fileSystem.GetFullPath(file));
            Assert.True(fileSystem.IsPathFullyQualified(fileSystem.GetFullPath(file)));
            Assert.Equal(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)),
                fileSystem.ComputeSha256Hash(file)
            );
            Assert.False(fileSystem.IsSymbolicLink(file));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void NormalFileOperationsFollowAndEnumerateUnixSymbolicLinks()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var targetDirectory = Path.Combine(root, "target");
        var targetFile = Path.Combine(targetDirectory, "helper.txt");
        var fileLink = Path.Combine(root, "helper-link.txt");
        var directoryLink = Path.Combine(root, "target-link");

        try
        {
            Directory.CreateDirectory(targetDirectory);
            File.WriteAllText(targetFile, "helper contents");
            File.CreateSymbolicLink(fileLink, targetFile);
            Directory.CreateSymbolicLink(directoryLink, targetDirectory);

            Assert.Equal("helper contents", fileSystem.ReadAllText(fileLink));
            Assert.Equal(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(targetFile)),
                fileSystem.ComputeSha256Hash(fileLink)
            );
            Assert.Contains(
                fileLink,
                fileSystem.EnumerateFiles(root, "*.txt", SearchOption.AllDirectories)
            );
            Assert.Contains(directoryLink, fileSystem.EnumerateDirectories(root));
            Assert.Equal(
                [Path.Combine(directoryLink, "helper.txt")],
                fileSystem.EnumerateFiles(directoryLink, "*.txt").ToArray()
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void IntegritySnapshotBindsLinuxFileIdentityOwnerAndHash()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var file = Path.Combine(root, "helper");

        try
        {
            fileSystem.WriteAllText(file, "helper contents");
            fileSystem.SetUnixFileMode(
                file,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );

            var snapshot = fileSystem.CaptureFileIntegritySnapshot(file);

            Assert.Equal(Path.GetFullPath(file), snapshot.FullPath);
            Assert.Equal(fileSystem.GetCurrentOwner(), snapshot.Owner);
            Assert.Equal(fileSystem.GetUnixFileMode(file), snapshot.UnixFileMode);
            Assert.Equal(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)),
                snapshot.Sha256Hash
            );
            Assert.Equal(Path.GetFullPath(root), snapshot.TrustedParentDirectories[0].FullPath);
            var mutableHashCopy = snapshot.Sha256Hash;
            mutableHashCopy[0] ^= 0xff;
            Assert.True(fileSystem.FileMatchesIntegritySnapshot(file, snapshot));
            Assert.Equal(
                System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)),
                snapshot.Sha256Hash
            );

            fileSystem.WriteAllText(file, "replacement");

            Assert.False(fileSystem.FileMatchesIntegritySnapshot(file, snapshot));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void IntegritySnapshotUsesWeakPureDotNetValidationOnWindowsAndMacOs()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var file = Path.Combine(root, "helper");

        try
        {
            File.WriteAllText(file, "helper contents");

            var snapshot = fileSystem.CaptureFileIntegritySnapshot(file);

            Assert.Equal(Path.GetFullPath(file), snapshot.FullPath);
            Assert.Equal(new FileSystemOwner("weak:owner-unverified"), snapshot.Owner);
            Assert.StartsWith("weak-path:file:", snapshot.Identity.Value, StringComparison.Ordinal);
            Assert.NotEmpty(snapshot.TrustedParentDirectories);
            Assert.True(fileSystem.FileMatchesIntegritySnapshot(file, snapshot));

            File.WriteAllText(file, "replacement");

            Assert.False(fileSystem.FileMatchesIntegritySnapshot(file, snapshot));
            Assert.NotEmpty(fileSystem.CaptureTrustedParentDirectorySnapshots(file));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void IntegritySnapshotRejectsWeakDotComponentsBeforeNormalization()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var helper = Path.Combine(root, "helper");
        var targetDirectory = Path.Combine(root, "target");
        var helperViaDot = Path.Combine(root, ".", "helper");
        var helperViaDotDot = Path.Combine(targetDirectory, "..", "helper");

        try
        {
            File.WriteAllText(helper, "helper contents");
            Directory.CreateDirectory(targetDirectory);
            var snapshot = fileSystem.CaptureFileIntegritySnapshot(helper);

            Assert.Throws<IOException>(() => fileSystem.CaptureFileIntegritySnapshot(helperViaDot));
            Assert.Throws<IOException>(() =>
                fileSystem.CaptureFileIntegritySnapshot(helperViaDotDot)
            );
            Assert.Throws<IOException>(() =>
                fileSystem.CaptureTrustedParentDirectorySnapshots(helperViaDot)
            );
            Assert.Throws<IOException>(() =>
                fileSystem.CaptureTrustedParentDirectorySnapshots(helperViaDotDot)
            );
            Assert.False(fileSystem.FileMatchesIntegritySnapshot(helperViaDot, snapshot));
            Assert.False(fileSystem.FileMatchesIntegritySnapshot(helperViaDotDot, snapshot));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void IntegritySnapshotRejectsWeakWindowsWin32TrimmedComponentsBeforePathNormalization()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var helper = Path.Combine(root, "helper");
        var helperViaDotSpace = Path.Combine(root, ". ", "helper");
        var helperViaDotDotSpace = Path.Combine(root, ".. ", "helper");
        var helperViaTrailingSpace = Path.Combine(root, "directory ", "helper");
        var helperViaTrailingPeriod = Path.Combine(root, "directory.", "helper");
        const string uncHelperViaDotSpace = @"\\server\share\. \helper.exe";
        const string uncHelperViaDotDotSpace = @"\\server\share\.. \helper.exe";
        const string uncHelperViaTrailingSpace = @"\\server\share\directory \helper.exe";
        const string uncHelperViaTrailingPeriod = @"\\server\share\directory.\helper.exe";

        try
        {
            File.WriteAllText(helper, "helper contents");
            var snapshot = fileSystem.CaptureFileIntegritySnapshot(helper);

            Assert.Throws<IOException>(() =>
                fileSystem.CaptureFileIntegritySnapshot(helperViaDotSpace)
            );
            Assert.Throws<IOException>(() =>
                fileSystem.CaptureFileIntegritySnapshot(helperViaDotDotSpace)
            );
            Assert.Throws<IOException>(() =>
                fileSystem.CaptureFileIntegritySnapshot(helperViaTrailingSpace)
            );
            Assert.Throws<IOException>(() =>
                fileSystem.CaptureFileIntegritySnapshot(helperViaTrailingPeriod)
            );
            Assert.Throws<IOException>(() =>
                fileSystem.CaptureFileIntegritySnapshot(uncHelperViaDotSpace)
            );
            Assert.Throws<IOException>(() =>
                fileSystem.CaptureFileIntegritySnapshot(uncHelperViaDotDotSpace)
            );
            Assert.Throws<IOException>(() =>
                fileSystem.CaptureFileIntegritySnapshot(uncHelperViaTrailingSpace)
            );
            Assert.Throws<IOException>(() =>
                fileSystem.CaptureFileIntegritySnapshot(uncHelperViaTrailingPeriod)
            );
            Assert.Throws<IOException>(() =>
                fileSystem.CaptureTrustedParentDirectorySnapshots(helperViaDotSpace)
            );
            Assert.Throws<IOException>(() =>
                fileSystem.CaptureTrustedParentDirectorySnapshots(uncHelperViaDotSpace)
            );
            Assert.False(fileSystem.FileMatchesIntegritySnapshot(helperViaDotSpace, snapshot));
            Assert.False(fileSystem.FileMatchesIntegritySnapshot(uncHelperViaDotSpace, snapshot));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void IntegritySnapshotRejectsWeakWindowsAndMacOsFileReparsePointWhereSupported()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var target = Path.Combine(root, "helper");
        var link = Path.Combine(root, "helper-link");

        try
        {
            File.WriteAllText(target, "helper contents");
            if (!TryCreateFileSymbolicLink(link, target))
            {
                return;
            }

            Assert.Throws<IOException>(() => fileSystem.CaptureFileIntegritySnapshot(link));
            Assert.False(
                fileSystem.FileMatchesIntegritySnapshot(
                    link,
                    fileSystem.CaptureFileIntegritySnapshot(target)
                )
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void IntegritySnapshotRejectsWeakWindowsAndMacOsParentReparsePointWhereSupported()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var targetDirectory = Path.Combine(root, "target");
        var target = Path.Combine(targetDirectory, "helper");
        var directoryLink = Path.Combine(root, "target-link");
        var helperViaLink = Path.Combine(directoryLink, "helper");

        try
        {
            Directory.CreateDirectory(targetDirectory);
            File.WriteAllText(target, "helper contents");
            if (!TryCreateDirectorySymbolicLink(directoryLink, targetDirectory))
            {
                return;
            }

            Assert.Throws<IOException>(() =>
                fileSystem.CaptureFileIntegritySnapshot(helperViaLink)
            );
            Assert.Throws<IOException>(() =>
                fileSystem.CaptureTrustedParentDirectorySnapshots(helperViaLink)
            );
            Assert.False(
                fileSystem.FileMatchesIntegritySnapshot(
                    helperViaLink,
                    fileSystem.CaptureFileIntegritySnapshot(target)
                )
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void IntegritySnapshotFailsClosedOnUnsupportedPlatforms()
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var file = Path.Combine(root, "helper");

        try
        {
            File.WriteAllText(file, "helper contents");

            Assert.Throws<PlatformNotSupportedException>(() =>
                fileSystem.CaptureFileIntegritySnapshot(file)
            );
            Assert.Throws<PlatformNotSupportedException>(() =>
                fileSystem.CaptureTrustedParentDirectorySnapshots(file)
            );
            Assert.Throws<PlatformNotSupportedException>(() =>
                fileSystem.FileMatchesIntegritySnapshot(
                    file,
                    new FileIntegritySnapshot(
                        Path.GetFullPath(file),
                        new FileSystemEntryIdentity("unsupported"),
                        new FileSystemOwner("unsupported"),
                        0,
                        System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)),
                        []
                    )
                )
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void IntegritySnapshotRevalidationFailsWhenLinuxFileIdentityChanges()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var file = Path.Combine(root, "helper");

        try
        {
            File.WriteAllText(file, "helper contents");
            File.SetUnixFileMode(
                file,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );

            var snapshot = fileSystem.CaptureFileIntegritySnapshot(file);
            fileSystem.AtomicWriteAllText(file, "helper contents");

            Assert.Equal("helper contents", fileSystem.ReadAllText(file));
            Assert.Equal(fileSystem.GetCurrentOwner(), fileSystem.GetOwner(file));
            Assert.Equal(HelperExecutableMode, fileSystem.GetUnixFileMode(file));
            Assert.False(fileSystem.FileMatchesIntegritySnapshot(file, snapshot));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void IntegritySnapshotRevalidationFailsWhenLinuxAncestorDirectoryIsRecreated()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var ancestor = Path.Combine(root, "ancestor");
        var parent = Path.Combine(ancestor, "parent");
        var savedParent = Path.Combine(root, "saved-parent");
        var replacementAncestor = Path.Combine(root, "replacement-ancestor");
        var file = Path.Combine(parent, "helper");

        try
        {
            Directory.CreateDirectory(parent);
            SetTrustedDirectoryMode(parent);
            SetTrustedDirectoryMode(ancestor);
            Directory.CreateDirectory(replacementAncestor);
            SetTrustedDirectoryMode(replacementAncestor);
            File.WriteAllText(file, "helper contents");
            File.SetUnixFileMode(
                file,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );

            var snapshot = fileSystem.CaptureFileIntegritySnapshot(file);
            Directory.Move(parent, savedParent);
            Directory.Delete(ancestor);
            Directory.Move(replacementAncestor, ancestor);
            Directory.Move(savedParent, parent);

            Assert.False(fileSystem.FileMatchesIntegritySnapshot(file, snapshot));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void IntegritySnapshotRejectsLinuxSymbolicLinkParentComponents()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var targetDirectory = Path.Combine(root, "target");
        var target = Path.Combine(targetDirectory, "helper");
        var directoryLink = Path.Combine(root, "target-link");
        var helperViaLink = Path.Combine(directoryLink, "helper");

        try
        {
            Directory.CreateDirectory(targetDirectory);
            File.SetUnixFileMode(
                targetDirectory,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute
            );
            File.WriteAllText(target, "helper contents");
            File.SetUnixFileMode(
                target,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
            Directory.CreateSymbolicLink(directoryLink, targetDirectory);

            Assert.Throws<IOException>(() =>
                fileSystem.CaptureFileIntegritySnapshot(helperViaLink)
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void IntegritySnapshotRejectsLinuxDotOrDotDotComponentsBeforePathNormalization()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var helper = Path.Combine(root, "helper");
        var targetDirectory = Path.Combine(root, "target");
        var directoryLink = Path.Combine(root, "target-link");
        var helperViaDotDot = Path.Combine(directoryLink, "..", "helper");

        try
        {
            File.WriteAllText(helper, "helper contents");
            File.SetUnixFileMode(
                helper,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
            Directory.CreateDirectory(targetDirectory);
            Directory.CreateSymbolicLink(directoryLink, targetDirectory);
            var snapshot = fileSystem.CaptureFileIntegritySnapshot(helper);

            Assert.Throws<IOException>(() =>
                fileSystem.CaptureFileIntegritySnapshot(Path.Combine(root, ".", "helper"))
            );
            Assert.Throws<IOException>(() =>
                fileSystem.CaptureFileIntegritySnapshot(helperViaDotDot)
            );
            Assert.Throws<IOException>(() =>
                fileSystem.CaptureTrustedParentDirectorySnapshots(helperViaDotDot)
            );
            Assert.False(fileSystem.FileMatchesIntegritySnapshot(helperViaDotDot, snapshot));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void IntegritySnapshotRejectsLinuxSymbolicLink()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var target = Path.Combine(root, "helper");
        var link = Path.Combine(root, "helper-link");

        try
        {
            File.WriteAllText(target, "helper contents");
            File.SetUnixFileMode(
                target,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
            File.CreateSymbolicLink(link, target);

            Assert.Throws<IOException>(() => fileSystem.CaptureFileIntegritySnapshot(link));
            Assert.False(
                fileSystem.FileMatchesIntegritySnapshot(
                    link,
                    fileSystem.CaptureFileIntegritySnapshot(target)
                )
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupWrite)]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherWrite)]
    public void IntegritySnapshotRejectsGroupOrOtherWritableLinuxFile(UnixFileMode unsafeMode)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var file = Path.Combine(root, "helper");

        try
        {
            File.WriteAllText(file, "helper contents");
            File.SetUnixFileMode(file, unsafeMode);

            Assert.Throws<UnauthorizedAccessException>(() =>
                fileSystem.CaptureFileIntegritySnapshot(file)
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void IntegritySnapshotRejectsNonExecutableLinuxFile()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var file = Path.Combine(root, "helper");

        try
        {
            File.WriteAllText(file, "helper contents");
            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite);

            Assert.Throws<UnauthorizedAccessException>(() =>
                fileSystem.CaptureFileIntegritySnapshot(file)
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupExecute)]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherExecute)]
    public void IntegritySnapshotRejectsCurrentUserOwnedLinuxFileWithoutUserExecute(
        UnixFileMode unsafeMode
    )
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var file = Path.Combine(root, "helper");

        try
        {
            File.WriteAllText(file, "helper contents");
            File.SetUnixFileMode(file, unsafeMode);

            Assert.Throws<UnauthorizedAccessException>(() =>
                fileSystem.CaptureFileIntegritySnapshot(file)
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
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
    public void IntegritySnapshotRejectsGroupOrOtherWritableLinuxParentDirectory(
        UnixFileMode unsafeMode
    )
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var file = Path.Combine(root, "helper");

        try
        {
            File.WriteAllText(file, "helper contents");
            File.SetUnixFileMode(
                file,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
            File.SetUnixFileMode(root, unsafeMode);

            Assert.Throws<UnauthorizedAccessException>(() =>
                fileSystem.CaptureFileIntegritySnapshot(file)
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                File.SetUnixFileMode(
                    root,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                );
                Directory.Delete(root, recursive: true);
            }
        }
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
    public void IntegritySnapshotRejectsGroupOrOtherWritableLinuxAncestorDirectory(
        UnixFileMode unsafeMode
    )
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var child = Path.Combine(root, "child");
        var file = Path.Combine(child, "helper");

        try
        {
            Directory.CreateDirectory(child);
            File.SetUnixFileMode(
                child,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute
            );
            File.WriteAllText(file, "helper contents");
            File.SetUnixFileMode(
                file,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
            File.SetUnixFileMode(root, unsafeMode);

            Assert.Throws<UnauthorizedAccessException>(() =>
                fileSystem.CaptureFileIntegritySnapshot(file)
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                File.SetUnixFileMode(
                    root,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                );
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void IntegritySnapshotRevalidationIncludesLinuxFileMode()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var file = Path.Combine(root, "helper");

        try
        {
            File.WriteAllText(file, "helper contents");
            File.SetUnixFileMode(
                file,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );

            var snapshot = fileSystem.CaptureFileIntegritySnapshot(file);
            File.SetUnixFileMode(file, UnixFileMode.UserRead);

            Assert.False(fileSystem.FileMatchesIntegritySnapshot(file, snapshot));

            File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.GroupWrite);

            Assert.False(fileSystem.FileMatchesIntegritySnapshot(file, snapshot));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void IntegritySnapshotRevalidationIncludesLinuxParentDirectoryMode()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var file = Path.Combine(root, "helper");

        try
        {
            File.WriteAllText(file, "helper contents");
            File.SetUnixFileMode(
                file,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );

            var snapshot = fileSystem.CaptureFileIntegritySnapshot(file);
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            Assert.False(fileSystem.FileMatchesIntegritySnapshot(file, snapshot));

            File.SetUnixFileMode(
                root,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupWrite
            );

            Assert.False(fileSystem.FileMatchesIntegritySnapshot(file, snapshot));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                File.SetUnixFileMode(
                    root,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                );
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void IntegritySnapshotRevalidationIncludesLinuxRecreatedParentDirectoryIdentity()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var parent = Path.Combine(root, "parent");
        var replacementParent = Path.Combine(root, "replacement-parent");
        var file = Path.Combine(parent, "helper");

        try
        {
            Directory.CreateDirectory(parent);
            File.SetUnixFileMode(
                parent,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute
            );
            File.WriteAllText(file, "helper contents");
            File.SetUnixFileMode(
                file,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
            Directory.CreateDirectory(replacementParent);
            File.SetUnixFileMode(
                replacementParent,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute
            );

            var snapshot = fileSystem.CaptureFileIntegritySnapshot(file);
            Directory.Delete(parent, recursive: true);
            Directory.Move(replacementParent, parent);
            File.WriteAllText(file, "helper contents");
            File.SetUnixFileMode(
                file,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );

            Assert.False(fileSystem.FileMatchesIntegritySnapshot(file, snapshot));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void IntegritySnapshotRejectsLinuxDirectory()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var directory = Path.Combine(root, "helper-directory");

        try
        {
            Directory.CreateDirectory(directory);

            Assert.Throws<IOException>(() => fileSystem.CaptureFileIntegritySnapshot(directory));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void IsSymbolicLinkDetectsUnixFileSymlink()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var target = Path.Combine(root, "helper");
        var link = Path.Combine(root, "helper-link");

        try
        {
            File.WriteAllText(target, "helper contents");
            File.CreateSymbolicLink(link, target);

            Assert.True(fileSystem.IsSymbolicLink(link));
            Assert.False(fileSystem.IsSymbolicLink(target));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void DeleteDirectoryRemovesUnixDirectorySymlinkWithoutDeletingTarget()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var target = Path.Combine(root, "target");
        var link = Path.Combine(root, "target-link");
        var file = Path.Combine(target, "helper");

        try
        {
            Directory.CreateDirectory(target);
            File.WriteAllText(file, "helper contents");
            Directory.CreateSymbolicLink(link, target);

            fileSystem.DeleteDirectory(link);

            Assert.False(fileSystem.DirectoryExists(link));
            Assert.True(fileSystem.DirectoryExists(target));
            Assert.Equal("helper contents", File.ReadAllText(file));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void GetOwnerReturnsCurrentOwnerForCreatedLinuxFile()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var file = Path.Combine(root, "owned");

        try
        {
            fileSystem.WriteAllText(file, "owned contents");

            Assert.Equal(fileSystem.GetCurrentOwner(), fileSystem.GetOwner(file));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void AtomicWriteAllTextCanRestrictExistingUnixFileModeForSecrets()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var file = Path.Combine(root, "secret.txt");

        try
        {
            File.WriteAllText(file, "old");
            fileSystem.SetUnixFileMode(
                file,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.GroupRead
                    | UnixFileMode.OtherRead
            );

            fileSystem.AtomicWriteAllText(
                file,
                "new",
                options: AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly
            );

            Assert.Equal("new", fileSystem.ReadAllText(file));
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                fileSystem.GetUnixFileMode(file)
            );
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string CreateTestDirectory()
    {
        var baseDirectory = AppContext.BaseDirectory;
        if (OperatingSystem.IsLinux())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(home))
            {
                baseDirectory = Path.Combine(home, ".azureauth-credprovider-platform-tests");
            }
        }

        var path = Path.Combine(baseDirectory, "filesystem tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
        {
            if (OperatingSystem.IsLinux())
            {
                SetTrustedDirectoryModeIfExists(baseDirectory);
                SetTrustedDirectoryModeIfExists(Path.Combine(baseDirectory, "filesystem tests"));
            }

            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute
            );
        }

        return path;
    }

    private static bool TryCreateFileSymbolicLink(string linkPath, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool TryCreateDirectorySymbolicLink(string linkPath, string targetPath)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (PlatformNotSupportedException)
        {
            return false;
        }
    }

    [SupportedOSPlatform("linux")]
    private static void SetTrustedDirectoryMode(string path)
    {
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead
                | UnixFileMode.UserWrite
                | UnixFileMode.UserExecute
                | UnixFileMode.GroupRead
                | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead
                | UnixFileMode.OtherExecute
        );
    }

    [SupportedOSPlatform("linux")]
    private static void SetTrustedDirectoryModeIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            File.SetUnixFileMode(
                path,
                UnixFileMode.UserRead
                    | UnixFileMode.UserWrite
                    | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead
                    | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead
                    | UnixFileMode.OtherExecute
            );
        }
    }
}
