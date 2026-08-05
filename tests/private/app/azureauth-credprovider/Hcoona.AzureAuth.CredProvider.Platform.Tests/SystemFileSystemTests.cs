using System.Text;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class SystemFileSystemTests
{
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

    public static bool IsWindows => OperatingSystem.IsWindows();

    private static string CreateTestDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
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
}
