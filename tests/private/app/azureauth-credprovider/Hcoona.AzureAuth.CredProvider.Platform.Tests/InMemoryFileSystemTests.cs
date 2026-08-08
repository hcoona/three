using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class InMemoryFileSystemTests
{
    [Fact]
    public void NormalOperationsReadWriteEnumerateAndDelete()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        fileSystem.CreateDirectory("/root/nested");
        fileSystem.WriteAllText("/root/nested/value.txt", "value");
        fileSystem.AtomicWriteAllBytes("/root/nested/value.bin", [0, 1, 255]);

        Assert.True(fileSystem.FileExists("/root/nested/value.txt"));
        Assert.Equal("value", fileSystem.ReadAllText("/root/nested/value.txt"));
        Assert.Equal([0, 1, 255], fileSystem.ReadAllBytes("/root/nested/value.bin"));
        Assert.Equal(5, fileSystem.GetFileLength("/root/nested/value.txt"));
        Assert.Equal(
            ["/root/nested/value.txt"],
            fileSystem.EnumerateFiles("/root", "*.txt", SearchOption.AllDirectories)
        );
        Assert.Equal(["/root/nested"], fileSystem.EnumerateDirectories("/root"));

        fileSystem.DeleteFile("/root/nested/value.txt");
        fileSystem.DeleteDirectory("/root/nested", recursive: true);

        Assert.False(fileSystem.FileExists("/root/nested/value.txt"));
        Assert.False(fileSystem.DirectoryExists("/root/nested"));
    }

    [Fact]
    public void AtomicWriteCreatesParentsAndReplacesContents()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);

        fileSystem.AtomicWriteAllText("/root/missing/value.txt", "first");
        fileSystem.AtomicWriteAllText("/root/missing/value.txt", "second");

        Assert.Equal("second", fileSystem.ReadAllText("/root/missing/value.txt"));
        Assert.True(fileSystem.DirectoryExists("/root/missing"));
    }

    [Fact]
    public void OwnerOnlyAtomicWriteAppliesRequestedMode()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);

        fileSystem.AtomicWriteAllText(
            "/root/secret.txt",
            "secret",
            options: AtomicWriteOptions.RestrictUnixFileModeToOwnerOnly
        );

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            fileSystem.GetUnixFileMode("/root/secret.txt")
        );
    }

    [Fact]
    public void WindowsPathSemanticsAreCaseInsensitive()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Windows);
        fileSystem.CreateDirectory(@"C:\Root");
        fileSystem.WriteAllText(@"C:\Root\Value.txt", "value");

        Assert.True(fileSystem.FileExists(@"c:\root\value.txt"));
        Assert.Equal("value", fileSystem.ReadAllText(@"C:\ROOT\VALUE.TXT"));
    }

    [Fact]
    public void ExecutableFileCheckUsesPosixModesAndDefersOnWindows()
    {
        var posixFileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        posixFileSystem.CreateDirectory("/root");
        posixFileSystem.WriteAllText("/root/tool", "contents");

        Assert.False(posixFileSystem.IsExecutableFile("/root/tool"));

        posixFileSystem.SetUnixFileMode(
            "/root/tool",
            UnixFileMode.UserRead | UnixFileMode.UserExecute
        );

        Assert.True(posixFileSystem.IsExecutableFile("/root/tool"));

        var windowsFileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Windows);
        windowsFileSystem.CreateDirectory(@"C:\root");
        windowsFileSystem.WriteAllText(@"C:\root\tool.exe", "contents");

        Assert.True(windowsFileSystem.IsExecutableFile(@"C:\root\tool.exe"));
    }

    [Fact]
    public void MutationLockRejectsConcurrentHolderAndCanBeReacquired()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        var mutationLock = (IFileSystemMutationLock)fileSystem;

        using (mutationLock.AcquireMutationLock("/root/locks/config"))
        {
            Assert.Throws<IOException>(() =>
                mutationLock.AcquireMutationLock("/root/locks/config")
            );
        }

        using IDisposable reacquired = mutationLock.AcquireMutationLock("/root/locks/config");
    }
}
