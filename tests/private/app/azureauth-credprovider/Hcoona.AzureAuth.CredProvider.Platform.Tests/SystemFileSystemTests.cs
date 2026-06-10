using System.Runtime.Versioning;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class SystemFileSystemTests
{
    private const UnixFileMode HelperExecutableMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    public static bool IsLinux => OperatingSystem.IsLinux();
    public static bool IsMacOS => OperatingSystem.IsMacOS();
    public static bool IsWindows => OperatingSystem.IsWindows();
    public static bool IsKnownSupportedPlatform =>
        OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();

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
    public void WriteAllTextUsesUtf8WithoutBomByDefault()
    {
        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var file = Path.Combine(root, "utf8.txt");
        const string contents = "plain text";
        byte[] expectedBytes = System.Text.Encoding.UTF8.GetBytes(contents);

        try
        {
            fileSystem.WriteAllText(file, contents);

            Assert.Equal(expectedBytes, File.ReadAllBytes(file));
            Assert.Equal(
                System.Security.Cryptography.SHA256.HashData(expectedBytes),
                fileSystem.ComputeSha256Hash(file)
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
    public void AtomicWriteAllTextUsesUtf8WithoutBomByDefault()
    {
        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var file = Path.Combine(root, "atomic-utf8.txt");
        const string contents = "atomic text";
        byte[] expectedBytes = System.Text.Encoding.UTF8.GetBytes(contents);

        try
        {
            fileSystem.AtomicWriteAllText(file, contents);

            Assert.Equal(expectedBytes, File.ReadAllBytes(file));
            Assert.Equal(
                System.Security.Cryptography.SHA256.HashData(expectedBytes),
                fileSystem.ComputeSha256Hash(file)
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

    [Fact(Skip = "Not applicable on macOS.", SkipWhen = nameof(IsMacOS))]
    public void GetFileLengthReturnsRegularFileMetadataLength()
    {
        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var emptyFile = Path.Combine(root, "empty.lock");
        var nonEmptyFile = Path.Combine(root, "non-empty.lock");

        try
        {
            File.WriteAllBytes(emptyFile, []);
            File.WriteAllBytes(nonEmptyFile, [1, 2, 3]);

            Assert.Equal(0, fileSystem.GetFileLength(emptyFile));
            Assert.Equal(3, fileSystem.GetFileLength(nonEmptyFile));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact(Skip = "Not applicable on macOS.", SkipWhen = nameof(IsMacOS))]
    public void GetFileLengthRejectsFinalSymbolicLinkWhereSupported()
    {
        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var target = Path.Combine(root, "target.lock");
        var link = Path.Combine(root, "link.lock");

        try
        {
            File.WriteAllText(target, string.Empty);
            Assert.SkipWhen(
                !TryCreateFileSymbolicLink(link, target),
                "File symbolic links are unavailable in this environment."
            );

            Assert.ThrowsAny<IOException>(() => fileSystem.GetFileLength(link));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact(Skip = "Not applicable on macOS.", SkipWhen = nameof(IsMacOS))]
    public void GetFileLengthRejectsDirectory()
    {
        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var directory = Path.Combine(root, "directory.lock");

        try
        {
            Directory.CreateDirectory(directory);

            Assert.ThrowsAny<IOException>(() => fileSystem.GetFileLength(directory));
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
    public void AtomicWriteAllBytesRoundTripsNonUtf8Bytes()
    {
        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var file = Path.Combine(root, "bytes.bin");
        byte[] contents = [0x00, 0xff, 0xfe, 0x80, 0x41];

        try
        {
            fileSystem.AtomicWriteAllBytes(file, contents);
            contents[0] = 0x7f;
            byte[] readContents = fileSystem.ReadAllBytes(file);
            readContents[1] = 0x7e;

            Assert.Equal(
                new byte[] { 0x00, 0xff, 0xfe, 0x80, 0x41 },
                fileSystem.ReadAllBytes(file)
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
    public void AtomicWriteAllTextFailsClosedForFinalSymbolicLinkWhereSupported()
    {
        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var target = Path.Combine(root, "target.txt");
        var link = Path.Combine(root, "link.txt");

        try
        {
            File.WriteAllText(target, "target contents");
            Assert.SkipWhen(
                !TryCreateFileSymbolicLink(link, target),
                "Symlink creation unavailable."
            );

            Assert.Throws<IOException>(() => fileSystem.AtomicWriteAllText(link, "replacement"));
            Assert.Equal("target contents", File.ReadAllText(target));
            Assert.True(fileSystem.IsSymbolicLink(link));
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
    public void AtomicWriteAllTextFailsClosedForParentDirectorySymbolicLinkWhereSupported()
    {
        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var targetDirectory = Path.Combine(root, "target");
        var linkDirectory = Path.Combine(root, "link");
        var linkedFile = Path.Combine(linkDirectory, "created.txt");
        var targetFile = Path.Combine(targetDirectory, "created.txt");

        try
        {
            Directory.CreateDirectory(targetDirectory);
            Assert.SkipWhen(
                !TryCreateDirectorySymbolicLink(linkDirectory, targetDirectory),
                "Symlink creation unavailable."
            );

            Assert.Throws<NotSupportedException>(() =>
                fileSystem.AtomicWriteAllText(linkedFile, "secret contents")
            );
            Assert.False(File.Exists(targetFile));
            Assert.True(fileSystem.IsSymbolicLink(linkDirectory));
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

    [Fact(Skip = "Targets exotic platforms only.", SkipWhen = nameof(IsKnownSupportedPlatform))]
    public void DeleteFileFailsClosedOnUnsupportedPlatformsBeforeDeleting()
    {
        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var file = Path.Combine(root, "delete.txt");

        try
        {
            File.WriteAllText(file, "before");

            Assert.Throws<PlatformNotSupportedException>(() => fileSystem.DeleteFile(file));
            Assert.True(File.Exists(file));
            Assert.Equal("before", File.ReadAllText(file));
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

    [Fact(Skip = "Not applicable on macOS.", SkipWhen = nameof(IsMacOS))]
    public void AtomicWriteAllTextRechecksExpectationImmediatelyBeforeMutation()
    {
        var root = CreateTestDirectory();
        var file = Path.Combine(root, "conditional.txt");
        var fileSystem = new SystemFileSystem((checkpoint, path) =>
        {
            if (
                checkpoint == FileMutationCheckpoint.BeforeAtomicWriteMutation
                && string.Equals(path, Path.GetFullPath(file), StringComparison.Ordinal)
            )
            {
                File.WriteAllText(file, "concurrent");
            }
        });

        try
        {
            File.WriteAllText(file, "before");
            var expectation = FileMutationExpectation.Existing(ComputeSha256("before"));

            var exception = Assert.Throws<InvalidOperationException>(() =>
                fileSystem.AtomicWriteAllText(file, "after", expectation: expectation)
            );

            Assert.Contains("conflict", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Equal("concurrent", File.ReadAllText(file));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact(Skip = "Not applicable on macOS.", SkipWhen = nameof(IsMacOS))]
    public void ConditionalAtomicWriteExistingMissingTargetLeavesNoPersistentParentsOrLock()
    {
        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var created = Path.Combine(root, "created");
        var nested = Path.Combine(created, "nested");
        var file = Path.Combine(nested, "conditional.txt");
        var lockFile = Path.Combine(nested, ".azureauth-credprovider.fs.lock");
        var expectation = FileMutationExpectation.Existing(ComputeSha256("missing"));

        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                fileSystem.AtomicWriteAllText(file, "after", expectation: expectation)
            );

            Assert.Contains("conflict", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(created));
            Assert.False(Directory.Exists(nested));
            Assert.False(File.Exists(file));
            Assert.False(File.Exists(lockFile));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Theory(Skip = "Not applicable on macOS.", SkipWhen = nameof(IsMacOS))]
    [InlineData(nameof(IFileSystem.AtomicWriteAllText))]
    [InlineData(nameof(IFileSystem.AtomicWriteAllBytes))]
    public void ConditionalAtomicWriteRejectsParentSymlinkBeforeFollowingTargetForHash(
        string methodName
    )
    {
        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var outside = Path.Combine(root, "outside");
        var link = Path.Combine(root, "link");
        var externalFile = Path.Combine(outside, "target.txt");
        var targetPath = Path.Combine(link, "target.txt");
        var expectation = FileMutationExpectation.Existing(ComputeSha256("wrong-before-state"));

        try
        {
            Directory.CreateDirectory(outside);
            File.WriteAllText(externalFile, "external");
            Assert.SkipWhen(
                !TryCreateDirectorySymbolicLink(link, outside),
                "Directory symbolic-link creation unavailable."
            );

            var exception = Assert.Throws<NotSupportedException>(() =>
                InvokeAtomicWrite(fileSystem, methodName, targetPath, expectation)
            );

            Assert.Contains("symbolic-link", exception.Message, StringComparison.Ordinal);
            Assert.Equal("external", File.ReadAllText(externalFile));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact(Skip = "Linux-specific symlink race.", SkipUnless = nameof(IsLinux))]
    public void AtomicWriteAllTextDoesNotCreateExternalLockWhenParentSwappedToSymlinkBeforeLock()
    {
        var root = CreateTestDirectory();
        var parent = Path.Combine(root, "parent");
        var savedParent = Path.Combine(root, "saved-parent");
        var outside = Path.Combine(root, "outside");
        var file = Path.Combine(parent, "race.txt");
        var escapedLock = Path.Combine(outside, ".azureauth-credprovider.fs.lock");
        var swapped = false;
        var fileSystem = new SystemFileSystem((checkpoint, path) =>
        {
            if (
                checkpoint == FileMutationCheckpoint.BeforeMutationLock
                && string.Equals(path, Path.GetFullPath(file), StringComparison.Ordinal)
                && !swapped
            )
            {
                swapped = true;
                Directory.Move(parent, savedParent);
                Directory.CreateSymbolicLink(parent, outside);
            }
        });

        try
        {
            Directory.CreateDirectory(parent);
            Directory.CreateDirectory(outside);

            Assert.Throws<IOException>(() => fileSystem.AtomicWriteAllText(file, "after"));

            Assert.True(swapped);
            Assert.False(File.Exists(escapedLock));
            Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
            Assert.False(File.Exists(Path.Combine(savedParent, "race.txt")));
        }
        finally
        {
            if (File.GetAttributes(parent).HasFlag(FileAttributes.ReparsePoint))
            {
                Directory.Delete(parent);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact(Skip = "Linux-specific symlink race.", SkipUnless = nameof(IsLinux))]
    public void AtomicWriteAllTextDoesNotCreateExternalLockWhenAncestorSwappedToSymlinkBeforeLock()
    {
        var root = CreateTestDirectory();
        var ancestor = Path.Combine(root, "ancestor");
        var savedAncestor = Path.Combine(root, "saved-ancestor");
        var parent = Path.Combine(ancestor, "parent");
        var outside = Path.Combine(root, "outside");
        var outsideParent = Path.Combine(outside, "parent");
        var file = Path.Combine(parent, "race.txt");
        var escapedLock = Path.Combine(outsideParent, ".azureauth-credprovider.fs.lock");
        var swapped = false;
        var fileSystem = new SystemFileSystem((checkpoint, path) =>
        {
            if (
                checkpoint == FileMutationCheckpoint.BeforeMutationLock
                && string.Equals(path, Path.GetFullPath(file), StringComparison.Ordinal)
                && !swapped
            )
            {
                swapped = true;
                Directory.Move(ancestor, savedAncestor);
                Directory.CreateSymbolicLink(ancestor, outside);
            }
        });

        try
        {
            Directory.CreateDirectory(parent);
            Directory.CreateDirectory(outsideParent);

            Assert.Throws<IOException>(() => fileSystem.AtomicWriteAllText(file, "after"));

            Assert.True(swapped);
            Assert.False(File.Exists(escapedLock));
            Assert.Empty(Directory.EnumerateFileSystemEntries(outsideParent));
            Assert.False(File.Exists(Path.Combine(savedAncestor, "parent", "race.txt")));
        }
        finally
        {
            if (File.GetAttributes(ancestor).HasFlag(FileAttributes.ReparsePoint))
            {
                Directory.Delete(ancestor);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact(Skip = "Linux-specific symlink race.", SkipUnless = nameof(IsLinux))]
    public void AtomicWriteAllTextDeletesTemporaryFileWhenParentSwappedToSymlinkAfterTempWrite()
    {
        var root = CreateTestDirectory();
        var parent = Path.Combine(root, "parent");
        var savedParent = Path.Combine(root, "saved-parent");
        var outside = Path.Combine(root, "outside");
        var file = Path.Combine(parent, "secret.txt");
        var escapedFile = Path.Combine(outside, "secret.txt");
        var swapped = false;
        var fileSystem = new SystemFileSystem((checkpoint, path) =>
        {
            if (
                checkpoint == FileMutationCheckpoint.BeforeAtomicWriteMutation
                && string.Equals(path, Path.GetFullPath(file), StringComparison.Ordinal)
                && !swapped
            )
            {
                swapped = true;
                Directory.Move(parent, savedParent);
                Directory.CreateSymbolicLink(parent, outside);
            }
        });

        try
        {
            Directory.CreateDirectory(parent);
            Directory.CreateDirectory(outside);

            var exception = Assert.Throws<NotSupportedException>(() =>
                fileSystem.AtomicWriteAllText(file, "secret text")
            );

            Assert.Contains("symbolic-link", exception.Message, StringComparison.Ordinal);
            Assert.True(swapped);
            Assert.False(File.Exists(Path.Combine(savedParent, "secret.txt")));
            Assert.False(File.Exists(escapedFile));
            Assert.Empty(
                Directory.EnumerateFiles(savedParent, "*.tmp", SearchOption.AllDirectories)
            );
            Assert.Empty(Directory.EnumerateFiles(outside, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            if (File.GetAttributes(parent).HasFlag(FileAttributes.ReparsePoint))
            {
                Directory.Delete(parent);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact(Skip = "Linux-specific symlink race.", SkipUnless = nameof(IsLinux))]
    public void AtomicWriteAllBytesDeletesTemporaryFileWhenParentSwappedToSymlinkAfterTempWrite()
    {
        var root = CreateTestDirectory();
        var parent = Path.Combine(root, "parent");
        var savedParent = Path.Combine(root, "saved-parent");
        var outside = Path.Combine(root, "outside");
        var file = Path.Combine(parent, "secret.bin");
        var escapedFile = Path.Combine(outside, "secret.bin");
        var swapped = false;
        var fileSystem = new SystemFileSystem((checkpoint, path) =>
        {
            if (
                checkpoint == FileMutationCheckpoint.BeforeAtomicWriteMutation
                && string.Equals(path, Path.GetFullPath(file), StringComparison.Ordinal)
                && !swapped
            )
            {
                swapped = true;
                Directory.Move(parent, savedParent);
                Directory.CreateSymbolicLink(parent, outside);
            }
        });

        try
        {
            Directory.CreateDirectory(parent);
            Directory.CreateDirectory(outside);

            var exception = Assert.Throws<NotSupportedException>(() =>
                fileSystem.AtomicWriteAllBytes(file, [1, 2, 3, 4])
            );

            Assert.Contains("symbolic-link", exception.Message, StringComparison.Ordinal);
            Assert.True(swapped);
            Assert.False(File.Exists(Path.Combine(savedParent, "secret.bin")));
            Assert.False(File.Exists(escapedFile));
            Assert.Empty(
                Directory.EnumerateFiles(savedParent, "*.tmp", SearchOption.AllDirectories)
            );
            Assert.Empty(Directory.EnumerateFiles(outside, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            if (File.GetAttributes(parent).HasFlag(FileAttributes.ReparsePoint))
            {
                Directory.Delete(parent);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact(Skip = "Linux-specific symlink race.", SkipUnless = nameof(IsLinux))]
    public void AtomicWriteAllTextDeletesTemporaryFileWhenTargetSwappedToSymlinkAfterTempWrite()
    {
        var root = CreateTestDirectory();
        var parent = Path.Combine(root, "parent");
        var file = Path.Combine(parent, "secret.txt");
        var outside = Path.Combine(root, "outside");
        var externalFile = Path.Combine(outside, "external.txt");
        var swapped = false;
        var fileSystem = new SystemFileSystem((checkpoint, path) =>
        {
            if (
                checkpoint == FileMutationCheckpoint.BeforeAtomicWriteMutation
                && string.Equals(path, Path.GetFullPath(file), StringComparison.Ordinal)
                && !swapped
            )
            {
                swapped = true;
                File.Delete(file);
                File.CreateSymbolicLink(file, externalFile);
            }
        });

        try
        {
            Directory.CreateDirectory(parent);
            Directory.CreateDirectory(outside);
            File.WriteAllText(file, "original");
            File.WriteAllText(externalFile, "external");

            var exception = Assert.Throws<IOException>(() =>
                fileSystem.AtomicWriteAllText(file, "secret text")
            );

            Assert.Contains("plain file or missing", exception.Message, StringComparison.Ordinal);
            Assert.True(swapped);
            Assert.Equal("external", File.ReadAllText(externalFile));
            Assert.True(fileSystem.IsSymbolicLink(file));
            Assert.Empty(Directory.EnumerateFiles(parent, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact(Skip = "Linux-specific symlink race.", SkipUnless = nameof(IsLinux))]
    public void AtomicWriteAllBytesDeletesTemporaryFileWhenTargetSwappedToSymlinkAfterTempWrite()
    {
        var root = CreateTestDirectory();
        var parent = Path.Combine(root, "parent");
        var file = Path.Combine(parent, "secret.bin");
        var outside = Path.Combine(root, "outside");
        var externalFile = Path.Combine(outside, "external.bin");
        byte[] externalContents = [9, 8, 7, 6];
        var swapped = false;
        var fileSystem = new SystemFileSystem((checkpoint, path) =>
        {
            if (
                checkpoint == FileMutationCheckpoint.BeforeAtomicWriteMutation
                && string.Equals(path, Path.GetFullPath(file), StringComparison.Ordinal)
                && !swapped
            )
            {
                swapped = true;
                File.Delete(file);
                File.CreateSymbolicLink(file, externalFile);
            }
        });

        try
        {
            Directory.CreateDirectory(parent);
            Directory.CreateDirectory(outside);
            File.WriteAllBytes(file, [1, 2, 3, 4]);
            File.WriteAllBytes(externalFile, externalContents);

            var exception = Assert.Throws<IOException>(() =>
                fileSystem.AtomicWriteAllBytes(file, [5, 6, 7, 8])
            );

            Assert.Contains("plain file or missing", exception.Message, StringComparison.Ordinal);
            Assert.True(swapped);
            Assert.Equal(externalContents, File.ReadAllBytes(externalFile));
            Assert.True(fileSystem.IsSymbolicLink(file));
            Assert.Empty(Directory.EnumerateFiles(parent, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact(Skip = "Linux symlink semantics required.", SkipUnless = nameof(IsLinux))]
    public void AtomicWriteAllTextRejectsPreExistingLinuxLockSymlinkWithoutMutatingExternalFile()
    {
        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var parent = Path.Combine(root, "parent");
        var outside = Path.Combine(root, "outside");
        var file = Path.Combine(parent, "target.txt");
        var lockFile = Path.Combine(parent, ".azureauth-credprovider.fs.lock");
        var externalLock = Path.Combine(outside, "external-lock.txt");

        try
        {
            Directory.CreateDirectory(parent);
            Directory.CreateDirectory(outside);
            File.WriteAllText(externalLock, "external lock before");
            File.CreateSymbolicLink(lockFile, externalLock);

            Assert.Throws<IOException>(() => fileSystem.AtomicWriteAllText(file, "after"));

            Assert.False(File.Exists(file));
            Assert.Equal("external lock before", File.ReadAllText(externalLock));
            Assert.Equal(
                externalLock,
                File.ResolveLinkTarget(lockFile, returnFinalTarget: false)?.FullName
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

    [Fact(Skip = "Non-Windows symlink test.", SkipWhen = nameof(IsWindows))]
    public void AtomicWriteAllTextRejectsMissingParentSymlinkEscapeBeforeWriting()
    {
        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var config = Path.Combine(root, "config");
        var outside = Path.Combine(root, "outside");
        var link = Path.Combine(config, "link");
        var escapedDirectory = Path.Combine(outside, "nested");
        var escapedFile = Path.Combine(escapedDirectory, "escape.txt");
        var escapedLock = Path.Combine(escapedDirectory, ".azureauth-credprovider.fs.lock");
        var fileViaLink = Path.Combine(link, "nested", "escape.txt");

        try
        {
            Directory.CreateDirectory(config);
            Directory.CreateDirectory(outside);
            Assert.SkipWhen(
                !TryCreateDirectorySymbolicLink(link, outside),
                "Symlink creation unavailable."
            );

            var exception = Assert.Throws<NotSupportedException>(() =>
                fileSystem.AtomicWriteAllText(fileViaLink, "secret")
            );

            Assert.Contains("symbolic-link", exception.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(escapedDirectory));
            Assert.False(File.Exists(escapedFile));
            Assert.False(File.Exists(escapedLock));
            Assert.Empty(Directory.EnumerateFiles(outside, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact(Skip = "Non-Windows symlink test.", SkipWhen = nameof(IsWindows))]
    public void DeleteFileRejectsParentSymlinkEscapeBeforeCreatingLock()
    {
        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var config = Path.Combine(root, "config");
        var outside = Path.Combine(root, "outside");
        var link = Path.Combine(config, "link");
        var escapedFile = Path.Combine(outside, "delete.txt");
        var escapedLock = Path.Combine(outside, ".azureauth-credprovider.fs.lock");
        var fileViaLink = Path.Combine(link, "delete.txt");

        try
        {
            Directory.CreateDirectory(config);
            Directory.CreateDirectory(outside);
            File.WriteAllText(escapedFile, "outside");
            Assert.SkipWhen(
                !TryCreateDirectorySymbolicLink(link, outside),
                "Symlink creation unavailable."
            );

            var exception = Assert.Throws<NotSupportedException>(() =>
                fileSystem.DeleteFile(fileViaLink)
            );

            Assert.Contains("symbolic-link", exception.Message, StringComparison.Ordinal);
            Assert.Equal("outside", File.ReadAllText(escapedFile));
            Assert.False(File.Exists(escapedLock));
            Assert.Empty(Directory.EnumerateFiles(outside, "*.tmp", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact(Skip = "macOS-specific behavior.", SkipUnless = nameof(IsMacOS))]
    public void ConditionalMutationsFailClosedOnMacOs()
    {
        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var file = Path.Combine(root, "conditional.txt");

        try
        {
            File.WriteAllText(file, "before");
            var expectation = FileMutationExpectation.Existing(ComputeSha256("before"));

            Assert.Throws<PlatformNotSupportedException>(() =>
                fileSystem.AtomicWriteAllText(file, "after", expectation: expectation)
            );
            Assert.Throws<PlatformNotSupportedException>(() =>
                fileSystem.DeleteFile(file, expectation)
            );
            Assert.Equal("before", File.ReadAllText(file));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact(Skip = "macOS-specific behavior.", SkipUnless = nameof(IsMacOS))]
    public void MutationLockRejectsPreExistingMacOsLockSymlinkWithoutMutatingExternalFile()
    {
        var fileSystem = new SystemFileSystem();
        var root = CreateTestDirectory();
        var parent = Path.Combine(root, "parent");
        var outside = Path.Combine(root, "outside");
        var lockFile = Path.Combine(parent, ".azureauth-credprovider.fs.lock");
        var externalLock = Path.Combine(outside, "external-lock.txt");

        try
        {
            Directory.CreateDirectory(parent);
            Directory.CreateDirectory(outside);
            File.WriteAllText(externalLock, "external lock before");
            Assert.SkipWhen(
                !TryCreateFileSymbolicLink(lockFile, externalLock),
                "Symlink creation unavailable."
            );

            Assert.Throws<IOException>(() =>
                ((IFileSystemMutationLock)fileSystem).AcquireMutationLock(parent).Dispose()
            );

            Assert.Equal("external lock before", File.ReadAllText(externalLock));
            Assert.True(fileSystem.IsSymbolicLink(lockFile));
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
            Assert.SkipWhen(
                !TryCreateFileSymbolicLink(link, target),
                "Symlink creation unavailable."
            );

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

    private static string ComputeSha256(string contents)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(contents)
        );
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void InvokeAtomicWrite(
        SystemFileSystem fileSystem,
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
