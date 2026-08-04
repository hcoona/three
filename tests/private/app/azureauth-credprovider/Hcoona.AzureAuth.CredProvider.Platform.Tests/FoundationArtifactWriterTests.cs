using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hcoona.AzureAuth.CredProvider.Platform.Packaging;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class FoundationArtifactWriterTests
{
    [Fact]
    public void ManifestHashesFilesWithSha256AndDoesNotIncludeSourcePaths()
    {
        using TestDirectory testDirectory = TestDirectory.Create();
        string source = testDirectory.WriteFile("local output/contracts.dll", "contract bytes");

        FoundationArtifactPackage package = CreatePackage(
            testDirectory,
            new FoundationArtifactInput(source, "Contracts/contracts.dll")
        );

        FoundationArtifactFile file = Assert.Single(package.Manifest.Files);
        Assert.Equal("Contracts/contracts.dll", file.Path);
        Assert.Equal("contract bytes".Length, file.Length);
        Assert.Equal(Sha256Hex("contract bytes"), file.Sha256);
        string manifestJson = Encoding.UTF8.GetString(package.ManifestBytes);
        Assert.DoesNotContain(testDirectory.Root, manifestJson, StringComparison.Ordinal);
        Assert.DoesNotContain(source, manifestJson, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateSerializesManifestWhenJsonReflectionIsDisabled()
    {
        Assert.False(JsonSerializer.IsReflectionEnabledByDefault);

        using TestDirectory testDirectory = TestDirectory.Create();
        string source = testDirectory.WriteFile("platform.dll", "platform");

        FoundationArtifactPackage package = CreatePackage(
            testDirectory,
            new FoundationArtifactInput(source, "Platform/platform.dll")
        );

        Assert.NotEmpty(package.ManifestBytes);
        Assert.Contains(
            "\"files\"",
            Encoding.UTF8.GetString(package.ManifestBytes),
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("/absolute/file.dll")]
    [InlineData("C:/absolute/file.dll")]
    [InlineData("C:relative/file.dll")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a/./b.dll")]
    [InlineData("a/../b.dll")]
    [InlineData("a//b.dll")]
    [InlineData("a/b.dll/")]
    [InlineData("a\\b.dll")]
    [InlineData("Platform/tool.dll.")]
    [InlineData("Platform/tool.dll ")]
    [InlineData("Platform. /tool.dll")]
    [InlineData("Platform/CON")]
    [InlineData("Platform/CONOUT$")]
    [InlineData("Platform/conin$.txt")]
    [InlineData("Platform/con.dll")]
    [InlineData("Platform/AUX.txt")]
    [InlineData("x/COM1.txt")]
    [InlineData("Platform/NUL .exe")]
    [InlineData("x/COM1 .txt")]
    [InlineData("x/COM¹.txt")]
    [InlineData("x/lpt²")]
    [InlineData("x/lpt9")]
    [InlineData("Platform/tool<1>.dll")]
    [InlineData("Platform/tool>1.dll")]
    [InlineData("Platform/tool\"1.dll")]
    [InlineData("Platform/tool|1.dll")]
    [InlineData("Platform/tool?1.dll")]
    [InlineData("Platform/tool*1.dll")]
    [InlineData("Platform/tool\u001F1.dll")]
    [InlineData("Platform/tool\u007F1.dll")]
    [InlineData("Platform/tool\u009F1.dll")]
    [InlineData("Platform/tool\u202E1.dll")]
    [InlineData("Platform/tool\u200D1.dll")]
    public void SafeRelativePathsRejectUnsafeArchivePaths(string path)
    {
        Assert.Throws<ArgumentException>(() => FoundationArtifactPath.EnsureSafeRelativePath(path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" win-x64")]
    [InlineData("win-x64 ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("win/../x64")]
    [InlineData("win\\x64")]
    [InlineData("win:x64")]
    [InlineData("win/x64")]
    [InlineData("win<x64")]
    [InlineData("win>x64")]
    [InlineData("win\"x64")]
    [InlineData("win|x64")]
    [InlineData("win?x64")]
    [InlineData("win*x64")]
    [InlineData("win\u001Fx64")]
    [InlineData("CON")]
    [InlineData("CONOUT$")]
    [InlineData("con.dll")]
    [InlineData("COM1.txt")]
    [InlineData("COM¹.txt")]
    [InlineData("lpt²")]
    [InlineData("lpt9")]
    public void SafeTargetRidRejectsUnsafePathSegments(string rid)
    {
        Assert.Throws<ArgumentException>(() => FoundationArtifactPath.EnsureSafeTargetRid(rid));
    }

    [Theory]
    [InlineData("win-x64")]
    [InlineData("linux-musl-x64")]
    [InlineData("osx.13-arm64")]
    public void SafeTargetRidAcceptsKnownSafeRidSegments(string rid)
    {
        FoundationArtifactPath.EnsureSafeTargetRid(rid);
    }

    [Fact]
    public void CreateRejectsUnsafeTargetRid()
    {
        using TestDirectory testDirectory = TestDirectory.Create();
        string source = testDirectory.WriteFile("platform.dll", "platform");
        using FileStream stream = File.Create(testDirectory.PackagePath);

        Assert.Throws<ArgumentException>(() =>
            FoundationArtifactWriter.Create(
                stream,
                [new FoundationArtifactInput(source, "Platform/platform.dll")],
                CreateOptions(targetRid: "CON.dll")
            )
        );
    }

    [Fact]
    public void CreateRejectsDuplicateArtifactPaths()
    {
        using TestDirectory testDirectory = TestDirectory.Create();
        string first = testDirectory.WriteFile("first.dll", "first");
        string second = testDirectory.WriteFile("second.dll", "second");

        Assert.Throws<ArgumentException>(() =>
            CreatePackage(
                testDirectory,
                new FoundationArtifactInput(first, "Platform/output.dll"),
                new FoundationArtifactInput(second, "Platform/output.dll")
            )
        );
    }

    [Fact]
    public void CreateRejectsCaseAmbiguousArtifactPaths()
    {
        using TestDirectory testDirectory = TestDirectory.Create();
        string first = testDirectory.WriteFile("Foo.dll", "first");
        string second = testDirectory.WriteFile("foo.dll", "second");

        Assert.Throws<ArgumentException>(() =>
            CreatePackage(
                testDirectory,
                new FoundationArtifactInput(first, "Platform/Foo.dll"),
                new FoundationArtifactInput(second, "Platform/foo.dll")
            )
        );
    }

    [Fact]
    public void CreateRejectsReservedManifestArtifactPath()
    {
        using TestDirectory testDirectory = TestDirectory.Create();
        string source = testDirectory.WriteFile("manifest.json", "shadow");

        Assert.Throws<ArgumentException>(() =>
            CreatePackage(
                testDirectory,
                new FoundationArtifactInput(source, FoundationArtifactWriter.ManifestArchivePath)
            )
        );
    }

    [Fact]
    public void ManifestAndArchiveUseDeterministicOrdering()
    {
        using TestDirectory testDirectory = TestDirectory.Create();
        string platform = testDirectory.WriteFile("platform.dll", "platform");
        string contracts = testDirectory.WriteFile("contracts.dll", "contracts");

        FoundationArtifactPackage package = CreatePackage(
            testDirectory,
            new FoundationArtifactInput(platform, "Platform/platform.dll"),
            new FoundationArtifactInput(contracts, "Contracts/contracts.dll")
        );

        Assert.Equal(
            ["Contracts/contracts.dll", "Platform/platform.dll"],
            package.Manifest.Files.Select(file => file.Path).ToArray()
        );
        Assert.Equal(
            ["manifest.json", "Contracts/contracts.dll", "Platform/platform.dll"],
            ReadArchiveEntries(testDirectory.PackagePath).Select(entry => entry.FullName).ToArray()
        );
    }

    [Fact]
    public void ArchiveContentsIncludeManifestAndCurrentFoundationOutputs()
    {
        using TestDirectory testDirectory = TestDirectory.Create();
        string contracts = testDirectory.WriteFile("bin/contracts.dll", "contracts");
        string platform = testDirectory.WriteFile("bin/platform.dll", "platform");

        FoundationArtifactPackage package = CreatePackage(
            testDirectory,
            new FoundationArtifactInput(
                contracts,
                "Contracts/Hcoona.AzureAuth.CredProvider.Contracts.dll"
            ),
            new FoundationArtifactInput(
                platform,
                "Platform/Hcoona.AzureAuth.CredProvider.Platform.dll"
            )
        );

        using var archive = ZipFile.OpenRead(testDirectory.PackagePath);
        Assert.NotNull(archive.GetEntry(FoundationArtifactWriter.ManifestArchivePath));
        Assert.Equal(
            "contracts",
            ReadEntryText(archive, "Contracts/Hcoona.AzureAuth.CredProvider.Contracts.dll")
        );
        Assert.Equal(
            "platform",
            ReadEntryText(archive, "Platform/Hcoona.AzureAuth.CredProvider.Platform.dll")
        );
        Assert.Equal(2, package.Manifest.Files.Count);
    }

    [Fact]
    public void ManifestMarksArtifactAsInternalNonReleaseAndUnsigned()
    {
        using TestDirectory testDirectory = TestDirectory.Create();
        string source = testDirectory.WriteFile("platform.dll", "platform");

        FoundationArtifactPackage package = CreatePackage(
            testDirectory,
            new FoundationArtifactInput(source, "Platform/platform.dll")
        );

        Assert.Equal(
            FoundationArtifactManifest.CurrentSchemaVersion,
            package.Manifest.SchemaVersion
        );
        Assert.Equal("azureauth-credprovider-foundation", package.Manifest.ArtifactName);
        Assert.Equal("test-os", package.Manifest.BuildOs);
        Assert.Equal("test-rid", package.Manifest.TargetRid);
        Assert.Equal("0.0.0-test", package.Manifest.ProductVersion);
        Assert.Equal("test-revision", package.Manifest.SourceRevision);
        Assert.Equal(
            "eng/scripts/azureauth-credprovider/New-FoundationArtifact.ps1",
            package.Manifest.ProducedBy
        );
        Assert.Equal("internal-non-release", package.Manifest.ReleaseStatus);
        Assert.Equal("unsigned", package.Manifest.SignatureStatus);
        Assert.True(package.Manifest.IsInternal);
        Assert.False(package.Manifest.IsRelease);
        Assert.False(package.Manifest.IsSigned);
    }

    [Fact]
    public void SameInputsCreateIdenticalArchiveBytes()
    {
        using TestDirectory testDirectory = TestDirectory.Create();
        string source = testDirectory.WriteFile("platform.dll", "platform");

        byte[] first = CreatePackageBytes(
            new FoundationArtifactInput(source, "Platform/platform.dll")
        );
        byte[] second = CreatePackageBytes(
            new FoundationArtifactInput(source, "Platform/platform.dll")
        );

        Assert.Equal(first, second);
    }

    [Fact]
    public void ManifestAndArchiveUseSameCapturedBytesWhenSourceMutates()
    {
        using TestDirectory testDirectory = TestDirectory.Create();
        string source = testDirectory.WriteFile("platform.dll", "original");

        FoundationArtifactPackage package = CreatePackage(
            testDirectory,
            InputsThatMutateSourceAfterCapture()
        );

        FoundationArtifactFile file = Assert.Single(package.Manifest.Files);
        Assert.Equal("original".Length, file.Length);
        Assert.Equal(Sha256Hex("original"), file.Sha256);
        using var archive = ZipFile.OpenRead(testDirectory.PackagePath);
        Assert.Equal("original", ReadEntryText(archive, "Platform/platform.dll"));
        Assert.Equal("mutated after capture", File.ReadAllText(source));

        IEnumerable<FoundationArtifactInput> InputsThatMutateSourceAfterCapture()
        {
            yield return new FoundationArtifactInput(source, "Platform/platform.dll");
            File.WriteAllText(source, "mutated after capture");
        }
    }

    private static FoundationArtifactPackage CreatePackage(
        TestDirectory testDirectory,
        params FoundationArtifactInput[] inputs
    )
    {
        using FileStream stream = File.Create(testDirectory.PackagePath);
        return FoundationArtifactWriter.Create(stream, inputs, CreateOptions());
    }

    private static FoundationArtifactPackage CreatePackage(
        TestDirectory testDirectory,
        IEnumerable<FoundationArtifactInput> inputs
    )
    {
        using FileStream stream = File.Create(testDirectory.PackagePath);
        return FoundationArtifactWriter.Create(stream, inputs, CreateOptions());
    }

    private static byte[] CreatePackageBytes(params FoundationArtifactInput[] inputs)
    {
        using var stream = new MemoryStream();
        FoundationArtifactWriter.Create(stream, inputs, CreateOptions());
        return stream.ToArray();
    }

    private static FoundationArtifactOptions CreateOptions()
    {
        return CreateOptions(targetRid: "test-rid");
    }

    private static FoundationArtifactOptions CreateOptions(string targetRid)
    {
        return new FoundationArtifactOptions(
            "azureauth-credprovider-foundation",
            "test-os",
            targetRid,
            "0.0.0-test",
            "test-revision"
        );
    }

    private static string Sha256Hex(string text)
    {
        return Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
            .ToLowerInvariant();
    }

    private static ZipArchiveEntry[] ReadArchiveEntries(string packagePath)
    {
        using var archive = ZipFile.OpenRead(packagePath);
        return archive.Entries.ToArray();
    }

    private static string ReadEntryText(ZipArchive archive, string entryPath)
    {
        ZipArchiveEntry entry = archive.GetEntry(entryPath)!;
        using Stream stream = entry.Open();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private sealed class TestDirectory : IDisposable
    {
        private TestDirectory(string root)
        {
            Root = root;
            PackagePath = Path.Combine(root, "package.zip");
        }

        public string Root { get; }

        public string PackagePath { get; }

        public static TestDirectory Create()
        {
            string root = Path.Combine(
                AppContext.BaseDirectory,
                "foundation-artifact-tests",
                Guid.NewGuid().ToString("N")
            );
            Directory.CreateDirectory(root);
            return new TestDirectory(root);
        }

        public string WriteFile(string relativePath, string contents)
        {
            string path = Path.Combine(Root, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                contents,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            );
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
