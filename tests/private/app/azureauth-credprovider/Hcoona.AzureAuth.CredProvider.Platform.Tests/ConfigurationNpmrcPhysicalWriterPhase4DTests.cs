using System.Text;
using Hcoona.AzureAuth.CredProvider.Contracts;
using Hcoona.AzureAuth.CredProvider.Platform.Configuration;
using Hcoona.AzureAuth.CredProvider.Platform.FileSystem;
using Hcoona.AzureAuth.CredProvider.Platform.Tests.TestDoubles;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class ConfigurationNpmrcPhysicalWriterPhase4DTests
{
    private const string Path = "/home/user/.npmrc";
    private static readonly byte[] Bom = [0xEF, 0xBB, 0xBF];

    [Fact]
    public void WritePreservesBomCrLfCommentsAndUnrelatedEntries()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        string selector = NpmCompatibleAuthSelectorPolicy.Create(resource).NpmAuthTokenKey;
        fileSystem.AtomicWriteAllBytes(
            Path,
            [.. Bom, .. Encoding.UTF8.GetBytes("# keep\r\nfund=false\r\n")]
        );
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);

        writer.Write(
            CreateRequest(CreateChange(selector, "token"), resource),
            TestContext.Current.CancellationToken
        );

        byte[] bytes = fileSystem.ReadAllBytes(Path);
        string text = Encoding.UTF8.GetString(bytes[3..]);
        Assert.True(bytes.AsSpan().StartsWith(Bom));
        Assert.Contains("# keep\r\n", text, StringComparison.Ordinal);
        Assert.Contains("fund=false\r\n", text, StringComparison.Ordinal);
        Assert.Contains($"{selector}=token", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", text.Replace("\r\n", "", StringComparison.Ordinal));
    }

    [Fact]
    public void RemoveDeletesOnlyTheOwnedSelectorEvenAfterValueDrift()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        string selector = NpmCompatibleAuthSelectorPolicy.Create(resource).NpmAuthTokenKey;
        fileSystem.AtomicWriteAllText(
            Path,
            $"{selector}=changed\nregistry=https://registry.npmjs.org/\n"
        );
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        ConfigurationChange remove = CreateChange(selector, null) with
        {
            Operation = ConfigurationChangeOperation.Remove,
        };

        writer.Write(
            CreateRequest(remove, resource, ConfigurationPlanOperation.Remove, [Owned(remove)]),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("registry=https://registry.npmjs.org/\n", fileSystem.ReadAllText(Path));
    }

    [Fact]
    public void ExistingUnownedSelectorIsRejected()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        string selector = NpmCompatibleAuthSelectorPolicy.Create(resource).NpmAuthTokenKey;
        fileSystem.AtomicWriteAllText(Path, $"{selector}=existing\n");
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);

        Assert.Throws<InvalidOperationException>(() =>
            writer.Write(
                CreateRequest(CreateChange(selector, "replacement"), resource),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal($"{selector}=existing\n", fileSystem.ReadAllText(Path));
    }

    [Theory]
    [InlineData("\"{0}\"=existing\n")]
    [InlineData("{0}[]=existing\n")]
    [InlineData("{0}[]\n")]
    public void ExistingDecodedOrArrayUnownedSelectorIsRejected(string existingTemplate)
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        string selector = NpmCompatibleAuthSelectorPolicy.Create(resource).NpmAuthTokenKey;
        string existing = string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            existingTemplate,
            selector
        );
        fileSystem.AtomicWriteAllText(Path, existing);
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);

        Assert.Throws<InvalidOperationException>(() =>
            writer.Write(
                CreateRequest(CreateChange(selector, "replacement"), resource),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(existing, fileSystem.ReadAllText(Path));
    }

    [Fact]
    public void WriteReplacesOwnedArraySelectorWithScalar()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        string selector = NpmCompatibleAuthSelectorPolicy.Create(resource).NpmAuthTokenKey;
        fileSystem.AtomicWriteAllText(Path, $"{selector}[]=existing\n");
        ConfigurationChange change = CreateChange(selector, "replacement");
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);

        writer.Write(
            CreateRequest(
                change,
                resource,
                ownership: [Owned(change)]
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal($"{selector}=replacement\n", fileSystem.ReadAllText(Path));
    }

    [Fact]
    public void IsSatisfiedRejectsNormalizedArrayAndScalarDuplicates()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        string selector = NpmCompatibleAuthSelectorPolicy.Create(resource).NpmAuthTokenKey;
        fileSystem.AtomicWriteAllText(
            Path,
            $"{selector}[]=existing\n{selector}=replacement\n"
        );
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);

        Assert.Throws<InvalidOperationException>(() =>
            writer.IsSatisfied(
                CreateRequest(CreateChange(selector, "requested-token"), resource),
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public void WriteInsertsManagedSelectorBeforeFirstSection()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        string selector = NpmCompatibleAuthSelectorPolicy.Create(resource).NpmAuthTokenKey;
        fileSystem.AtomicWriteAllText(
            Path,
            "registry=https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/\n"
                + "[ignored]\n"
                + "setting=value\n"
        );
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);
        ConfigurationPhysicalTargetWriterRequest request = CreateRequest(
            CreateChange(selector, "token"),
            resource
        );

        writer.Write(request, TestContext.Current.CancellationToken);

        Assert.Equal(
            "registry=https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/\n"
                + selector
                + "=token\n"
                + "[ignored]\n"
                + "setting=value\n",
            fileSystem.ReadAllText(Path)
        );
        Assert.True(writer.IsSatisfied(request, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void IsSatisfiedIgnoresSectionedSelector()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        string selector = NpmCompatibleAuthSelectorPolicy.Create(resource).NpmAuthTokenKey;
        fileSystem.AtomicWriteAllText(
            Path,
            "[ignored]\n" + selector + "=section-token\n"
        );
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);

        bool satisfied = writer.IsSatisfied(
            CreateRequest(CreateChange(selector, "requested-token"), resource),
            TestContext.Current.CancellationToken
        );

        Assert.False(satisfied);
    }

    [Fact]
    public void SecretWriteUsesOwnerOnlyMode()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        string selector = NpmCompatibleAuthSelectorPolicy.Create(resource).NpmAuthTokenKey;
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);

        writer.Write(
            CreateRequest(CreateChange(selector, "token"), resource),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            fileSystem.GetUnixFileMode(Path)
        );
    }

    [Fact]
    public void WriteConfigureAndUnconfigurePreserveNpmrcSymbolicLink()
    {
        const string LinkPath = "/home/user/.npmrc-link";
        const string TargetPath = "/home/user/actual.npmrc";
        const string Original = "registry=https://registry.npmjs.org/\n";
        var fileSystem = new RevalidatingLinkFileSystem(
            LinkPath,
            TargetPath,
            TargetPath
        );
        fileSystem.Inner.AtomicWriteAllText(TargetPath, Original);
        CanonicalResourceIdentity resource = CreateResource();
        string selector = NpmCompatibleAuthSelectorPolicy.Create(resource).NpmAuthTokenKey;
        ConfigurationChange apply = CreateChange(selector, "token", LinkPath);
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);

        writer.Write(
            CreateRequest(apply, resource),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, fileSystem.ResolutionCount);
        Assert.False(fileSystem.Inner.FileExists(LinkPath));
        Assert.Contains($"{selector}=token\n", fileSystem.Inner.ReadAllText(TargetPath));
        Assert.Contains(Original, fileSystem.Inner.ReadAllText(TargetPath));

        ConfigurationChange remove = apply with
        {
            Operation = ConfigurationChangeOperation.Remove,
            Value = null,
        };
        writer.Write(
            CreateRequest(
                remove,
                resource,
                ConfigurationPlanOperation.Remove,
                [Owned(apply)]
            ),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(4, fileSystem.ResolutionCount);
        Assert.False(fileSystem.Inner.FileExists(LinkPath));
        Assert.Equal(Original, fileSystem.Inner.ReadAllText(TargetPath));
    }

    [Fact]
    public void WriteConfigureAndUnconfigurePreserveOrdinaryNpmrcSymbolicLink()
    {
        string directory = ConfigurationPhysicalWriterSymlinkTestSupport.CreateDirectory(
            "npmrc-link"
        );
        string targetPath = System.IO.Path.Combine(directory, "actual.npmrc");
        string linkPath = System.IO.Path.Combine(directory, ".npmrc");
        const string Original = "registry=https://registry.npmjs.org/\n";
        File.WriteAllText(targetPath, Original);
        if (
            !ConfigurationPhysicalWriterSymlinkTestSupport.TryCreateFileSymbolicLink(
                linkPath,
                System.IO.Path.GetFileName(targetPath)
            )
        )
        {
            Directory.Delete(directory, recursive: true);
            return;
        }
        string? originalLinkTarget = new FileInfo(linkPath).LinkTarget;
        CanonicalResourceIdentity resource = CreateResource();
        string selector = NpmCompatibleAuthSelectorPolicy.Create(resource).NpmAuthTokenKey;
        ConfigurationChange apply = CreateChange(selector, "token", linkPath);
        var writer = new NpmrcPhysicalTargetWriter(new SystemFileSystem());

        try
        {
            writer.Write(
                CreateRequest(apply, resource),
                TestContext.Current.CancellationToken
            );

            Assert.Equal(originalLinkTarget, new FileInfo(linkPath).LinkTarget);
            Assert.Contains($"{selector}=token\n", File.ReadAllText(targetPath));
            Assert.Contains(Original, File.ReadAllText(targetPath));

            ConfigurationChange remove = apply with
            {
                Operation = ConfigurationChangeOperation.Remove,
                Value = null,
            };
            writer.Write(
                CreateRequest(
                    remove,
                    resource,
                    ConfigurationPlanOperation.Remove,
                    [Owned(apply)]
                ),
                TestContext.Current.CancellationToken
            );

            Assert.Equal(originalLinkTarget, new FileInfo(linkPath).LinkTarget);
            Assert.Equal(Original, File.ReadAllText(targetPath));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteConfigureFailsExplicitlyForDanglingNpmrcSymbolicLink()
    {
        const string LinkPath = "/home/user/.npmrc-link";
        const string Message = "The file link does not resolve to an existing file.";
        var fileSystem = new RevalidatingLinkFileSystem(
            LinkPath,
            new IOException(Message)
        );
        CanonicalResourceIdentity resource = CreateResource();
        string selector = NpmCompatibleAuthSelectorPolicy.Create(resource).NpmAuthTokenKey;
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);

        IOException exception = Assert.Throws<IOException>(() =>
            writer.Write(
                CreateRequest(CreateChange(selector, "token", LinkPath), resource),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(Message, exception.Message);
        Assert.Equal(1, fileSystem.ResolutionCount);
        Assert.False(fileSystem.Inner.FileExists(LinkPath));
    }

    [Fact]
    public void WriteConfigureFailsExplicitlyForOrdinaryDanglingNpmrcSymbolicLink()
    {
        string directory = ConfigurationPhysicalWriterSymlinkTestSupport.CreateDirectory(
            "npmrc-dangling"
        );
        string linkPath = System.IO.Path.Combine(directory, ".npmrc");
        const string MissingTarget = "missing.npmrc";
        if (
            !ConfigurationPhysicalWriterSymlinkTestSupport.TryCreateFileSymbolicLink(
                linkPath,
                MissingTarget
            )
        )
        {
            Directory.Delete(directory, recursive: true);
            return;
        }
        CanonicalResourceIdentity resource = CreateResource();
        string selector = NpmCompatibleAuthSelectorPolicy.Create(resource).NpmAuthTokenKey;
        var writer = new NpmrcPhysicalTargetWriter(new SystemFileSystem());

        try
        {
            IOException exception = Assert.Throws<IOException>(() =>
                writer.Write(
                    CreateRequest(CreateChange(selector, "token", linkPath), resource),
                    TestContext.Current.CancellationToken
                )
            );

            Assert.Contains(
                "does not resolve to an existing file",
                exception.Message,
                StringComparison.Ordinal
            );
            Assert.Equal(MissingTarget, new FileInfo(linkPath).LinkTarget);
            Assert.False(File.Exists(System.IO.Path.Combine(directory, MissingTarget)));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void WriteFailsWhenNpmrcLinkResolutionChangesBeforeAtomicWrite()
    {
        const string LinkPath = "/home/user/.npmrc-link";
        const string FirstTarget = "/home/user/first.npmrc";
        const string SecondTarget = "/home/user/second.npmrc";
        var fileSystem = new RevalidatingLinkFileSystem(
            LinkPath,
            FirstTarget,
            SecondTarget
        );
        fileSystem.Inner.AtomicWriteAllText(FirstTarget, "fund=false\n");
        fileSystem.Inner.AtomicWriteAllText(SecondTarget, "audit=false\n");
        CanonicalResourceIdentity resource = CreateResource();
        string selector = NpmCompatibleAuthSelectorPolicy.Create(resource).NpmAuthTokenKey;
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);

        IOException exception = Assert.Throws<IOException>(() =>
            writer.Write(
                CreateRequest(CreateChange(selector, "token", LinkPath), resource),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("link changed while it was being updated", exception.Message);
        Assert.Equal(2, fileSystem.ResolutionCount);
        Assert.Equal("fund=false\n", fileSystem.Inner.ReadAllText(FirstTarget));
        Assert.Equal("audit=false\n", fileSystem.Inner.ReadAllText(SecondTarget));
        Assert.False(fileSystem.Inner.FileExists(LinkPath));
    }

    [Fact]
    public void IsSatisfiedTreatsEmptyNpmAuthTokenAsMissing()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        string selector = NpmCompatibleAuthSelectorPolicy.Create(resource).NpmAuthTokenKey;
        const string Unrelated = "fund=false\n";
        fileSystem.AtomicWriteAllText(Path, $"{selector}=\n{Unrelated}");
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);

        bool satisfied = writer.IsSatisfied(
            CreateRequest(CreateChange(selector, "requested-token"), resource),
            TestContext.Current.CancellationToken
        );

        Assert.False(satisfied);
        Assert.Equal($"{selector}=\n{Unrelated}", fileSystem.ReadAllText(Path));
    }

    [Fact]
    public void IsSatisfiedTreatsNonEmptyNpmAuthTokenAsOpaquePresence()
    {
        var fileSystem = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        CanonicalResourceIdentity resource = CreateResource();
        string selector = NpmCompatibleAuthSelectorPolicy.Create(resource).NpmAuthTokenKey;
        const string OpaqueValue = "different-opaque-token";
        fileSystem.AtomicWriteAllText(Path, $"{selector}={OpaqueValue}\n");
        var writer = new NpmrcPhysicalTargetWriter(fileSystem);

        bool satisfied = writer.IsSatisfied(
            CreateRequest(CreateChange(selector, "requested-token"), resource),
            TestContext.Current.CancellationToken
        );

        Assert.True(satisfied);
        Assert.Equal($"{selector}={OpaqueValue}\n", fileSystem.ReadAllText(Path));
    }

    private static ConfigurationPhysicalTargetWriterRequest CreateRequest(
        ConfigurationChange change,
        CanonicalResourceIdentity resource,
        ConfigurationPlanOperation operation = ConfigurationPlanOperation.Apply,
        IReadOnlyList<ConfigurationOwnershipManifestEntry>? ownership = null
    ) =>
        new(operation, ConfigurationTargetKind.Npmrc, [change], ownership)
        {
            ResourceIdentity = resource,
        };

    private static ConfigurationChange CreateChange(
        string key,
        string? value,
        string path = Path
    ) =>
        new()
        {
            Operation = ConfigurationChangeOperation.Set,
            TargetKind = ConfigurationTargetKind.Npmrc,
            TargetPathOrName = path,
            Key = key,
            Value = value,
            RequiresOwnershipRecord = true,
            PreserveDeclarationsAndComments = true,
            IsSecretValue = true,
        };

    private static ConfigurationOwnershipManifestEntry Owned(ConfigurationChange change) =>
        new()
        {
            Sequence = 1,
            TargetKind = change.TargetKind,
            TargetPathOrName = change.TargetPathOrName,
            Key = change.Key,
        };

    private static CanonicalResourceIdentity CreateResource() =>
        CanonicalResourceIdentity.Create(
            "pkgs.dev.azure.com",
            "org",
            new Uri("https://pkgs.dev.azure.com/org/_packaging/feed/npm/registry/"),
            feed: "feed"
        );

}

internal sealed class RevalidatingLinkFileSystem : IFileSystem, IFileSystemLinkResolver
{
    private readonly string linkPath;
    private readonly string firstTarget;
    private readonly string secondTarget;
    private readonly IOException? resolutionFailure;

    public RevalidatingLinkFileSystem(
        string linkPath,
        string firstTarget,
        string secondTarget
    )
    {
        Inner = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        this.linkPath = linkPath;
        this.firstTarget = firstTarget;
        this.secondTarget = secondTarget;
    }

    public RevalidatingLinkFileSystem(string linkPath, IOException resolutionFailure)
    {
        Inner = new InMemoryFileSystem(InMemoryPathSemantics.Posix);
        this.linkPath = linkPath;
        firstTarget = string.Empty;
        secondTarget = string.Empty;
        this.resolutionFailure = resolutionFailure;
    }

    public InMemoryFileSystem Inner { get; }

    public int ResolutionCount { get; private set; }

    public bool FileExists(string path) => Inner.FileExists(path);

    public bool IsExecutableFile(string path) => Inner.IsExecutableFile(path);

    public bool DirectoryExists(string path) => Inner.DirectoryExists(path);

    public string GetFullPath(string path) => Inner.GetFullPath(path);

    public bool IsPathFullyQualified(string path) => Inner.IsPathFullyQualified(path);

    public string ReadAllText(string path, Encoding? encoding = null) =>
        Inner.ReadAllText(path, encoding);

    public byte[] ReadAllBytes(string path) => Inner.ReadAllBytes(path);

    public long GetFileLength(string path) => Inner.GetFileLength(path);

    public void WriteAllText(string path, string contents, Encoding? encoding = null) =>
        Inner.WriteAllText(path, contents, encoding);

    public void AtomicWriteAllText(
        string path,
        string contents,
        Encoding? encoding = null,
        AtomicWriteOptions options = AtomicWriteOptions.None
    ) => Inner.AtomicWriteAllText(path, contents, encoding, options);

    public void AtomicWriteAllBytes(
        string path,
        byte[] contents,
        AtomicWriteOptions options = AtomicWriteOptions.None
    ) => Inner.AtomicWriteAllBytes(path, contents, options);

    public UnixFileMode GetUnixFileMode(string path) => Inner.GetUnixFileMode(path);

    public void SetUnixFileMode(string path, UnixFileMode mode) =>
        Inner.SetUnixFileMode(path, mode);

    public void CreateDirectory(string path) => Inner.CreateDirectory(path);

    public void DeleteFile(string path) => Inner.DeleteFile(path);

    public void DeleteDirectory(string path, bool recursive = false) =>
        Inner.DeleteDirectory(path, recursive);

    public IEnumerable<string> EnumerateFiles(
        string path,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly
    ) => Inner.EnumerateFiles(path, searchPattern, searchOption);

    public IEnumerable<string> EnumerateDirectories(
        string path,
        string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly
    ) => Inner.EnumerateDirectories(path, searchPattern, searchOption);

    string IFileSystemLinkResolver.ResolveFilePathForWrite(string path)
    {
        Assert.Equal(linkPath, path);
        ResolutionCount++;
        if (resolutionFailure is not null)
        {
            throw resolutionFailure;
        }
        return ResolutionCount == 1 ? firstTarget : secondTarget;
    }
}

internal static class ConfigurationPhysicalWriterSymlinkTestSupport
{
    public static string CreateDirectory(string name)
    {
        string directory = System.IO.Path.Combine(
            AppContext.BaseDirectory,
            "configuration-writer-tests",
            name,
            Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static bool TryCreateFileSymbolicLink(string path, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(path, targetPath);
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
}
