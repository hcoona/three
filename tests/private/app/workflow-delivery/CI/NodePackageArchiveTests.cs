using WorkflowDelivery.Ecosystems;
using System.Formats.Tar;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.CI;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
[DoNotParallelize]
public sealed class NodePackageArchiveTests(TestContext context)
{
    [TestMethod]
    [DataRow("package/package.json")]
    [DataRow("package/dist/index.js")]
    public async Task ReadRequiresExactlyOneNonemptyRegularEntryPerRequiredMember(string path)
    {
        ArchiveMember[] required = PackageArchiveFixture.Required();
        ArchiveMember member = required.Single(item => item.Name == path);
        ArchiveMember other = required.Single(item => item.Name != path);

        await AssertRejectedAsync("lacks its required manifest/ESM payload", [other]);
        await AssertRejectedAsync(path, [.. required, member]);
        await AssertRejectedAsync(path, [other, member with { Bytes = [] }]);
    }

    [TestMethod]
    [DataRow("package/package.json", TarEntryType.SymbolicLink)]
    [DataRow("package/dist/index.js", TarEntryType.Directory)]
    public Task ReadRejectsNonFileRequiredMembers(string path, TarEntryType kind) =>
        AssertRejectedAsync(path, PackageArchiveFixture.Required()
            .Select(member => member.Name == path ? member with { Kind = kind } : member));

    [TestMethod]
    [DataRow("package/binding.gyp", TarEntryType.RegularFile)]
    [DataRow("package/.hooks", TarEntryType.Directory)]
    [DataRow("package/.hooks/install", TarEntryType.RegularFile)]
    public Task ReadRejectsNativeBuildMembers(string path, TarEntryType kind) =>
        AssertRejectedAsync("Unsupported npm native-build member",
            [.. PackageArchiveFixture.Required(), new(path, [1], kind)]);

    [TestMethod]
    public Task ReadRejectsOversizedManifest()
    {
        ArchiveMember[] required = PackageArchiveFixture.Required();
        required[0] = required[0] with { Bytes = new byte[NodePackageArchive.ManifestLimit + 1] };
        return AssertRejectedAsync("unsupported npm member: package/package.json", required);
    }

    [TestMethod]
    public Task ReadBoundsExpandedMember() =>
        AssertRejectedAsync("expanded size bound",
            [.. PackageArchiveFixture.Required(),
                new("package/expanded.bin", new byte[NodePackageArchive.ExpandedLimit])]);

    private async Task AssertRejectedAsync(string diagnostic, IEnumerable<ArchiveMember> members)
    {
        using var files = new ArchiveFiles();
        PackageArchiveFixture.Write(files.Archive, members);

        InvalidDataException error = await RejectAsync(files);
        Assert.Contains(diagnostic, error.Message);
        Assert.IsFalse(File.Exists(Path.Combine(files.Metadata, "dist", "index.js")));
    }

    [TestMethod]
    [DataRow(TarEntryFormat.Pax)]
    [DataRow(TarEntryFormat.Ustar)]
    [DataRow(TarEntryFormat.V7)]
    public async Task ReadAcceptsNativeFormatsAndConsumesRequiredStreams(TarEntryFormat format)
    {
        using var files = new ArchiveFiles();
        ArchiveMember[] required = PackageArchiveFixture.Required();
        required[1] = required[1] with { Bytes = new byte[NodePackageArchive.ManifestLimit] };
        PackageArchiveFixture.Write(files.Archive, required.Reverse(), format);

        await NodePackageArchive.ReadAsync(files.Archive, files.Metadata,
            context.CancellationToken);

        CollectionAssert.AreEqual(required[0].Bytes, await File.ReadAllBytesAsync(
            Path.Combine(files.Metadata, "package.json"), context.CancellationToken));
        Assert.AreEqual("package.json", Path.GetFileName(Assert.ContainsSingle(
            Directory.EnumerateFileSystemEntries(files.Metadata))));
    }

    [TestMethod]
    public async Task ReadBoundsExpandedBytesAfterTarEnd()
    {
        using var files = new ArchiveFiles();
        PackageArchiveFixture.Write(files.Archive, PackageArchiveFixture.Required(),
            trailingExpandedBytes: NodePackageArchive.ExpandedLimit);

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            NodePackageArchive.ReadAsync(files.Archive, files.Metadata, context.CancellationToken));

        Assert.Contains("expanded size bound", error.Message);
    }

    [TestMethod]
    public async Task ReadAcceptsTheManifestByteBoundary()
    {
        using var files = new ArchiveFiles();
        ArchiveMember[] required = PackageArchiveFixture.Required();
        required[0] = required[0] with { Bytes = new byte[NodePackageArchive.ManifestLimit] };
        PackageArchiveFixture.Write(files.Archive, required);

        await NodePackageArchive.ReadAsync(files.Archive, files.Metadata,
            context.CancellationToken);

        Assert.AreEqual(NodePackageArchive.ManifestLimit,
            new FileInfo(Path.Combine(files.Metadata, "package.json")).Length);
        Assert.AreEqual("package.json", Path.GetFileName(Assert.ContainsSingle(
            Directory.EnumerateFileSystemEntries(files.Metadata))));
    }

    [TestMethod]
    public async Task ReadRejectsTruncatedPackedPayload()
    {
        using var files = new ArchiveFiles();
        ArchiveMember[] required = PackageArchiveFixture.Required();
        byte[] payload = new byte[64 * 1024];
        new Random(73).NextBytes(payload);
        required[1] = required[1] with { Bytes = payload };
        PackageArchiveFixture.Write(files.Archive, required);
        using (var archive = new FileStream(files.Archive, FileMode.Open, FileAccess.Write))
            archive.SetLength(archive.Length / 2);

        await Assert.ThrowsAsync<IOException>(() => NodePackageArchive.ReadAsync(
            files.Archive, files.Metadata, context.CancellationToken));
    }

    [TestMethod]
    public async Task ReadAcceptsManySmallMembers()
    {
        using var files = new ArchiveFiles();
        IEnumerable<ArchiveMember> members = PackageArchiveFixture.Required().Concat(
            Enumerable.Range(0, 2048)
                .Select(index => new ArchiveMember("package/extra-" + index, [1])));
        PackageArchiveFixture.Write(files.Archive, members);

        await NodePackageArchive.ReadAsync(files.Archive, files.Metadata,
            context.CancellationToken);

        Assert.AreEqual("package.json", Path.GetFileName(Assert.ContainsSingle(
            Directory.EnumerateFileSystemEntries(files.Metadata))));
    }

    [TestMethod]
    public async Task ReadMaterializesOnlyTheFixedManifest()
    {
        using var files = new ArchiveFiles();
        ArchiveMember[] required = PackageArchiveFixture.Required();
        PackageArchiveFixture.Write(files.Archive, [
            new("../escaped.txt", [1]),
            new("package/unrelated-link", [], TarEntryType.SymbolicLink),
            new("package/../unrelated.txt", [1]), .. required,
        ]);

        byte[] original = await File.ReadAllBytesAsync(files.Archive, context.CancellationToken);

        await NodePackageArchive.ReadAsync(files.Archive, files.Metadata,
            context.CancellationToken);

        CollectionAssert.AreEqual(required[0].Bytes, await File.ReadAllBytesAsync(
            Path.Combine(files.Metadata, "package.json"), context.CancellationToken));
        Assert.AreEqual("package.json", Path.GetFileName(Assert.ContainsSingle(
            Directory.EnumerateFileSystemEntries(files.Metadata))));
        Assert.IsFalse(File.Exists(Path.Combine(files.Root, "escaped.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(files.Root, "unrelated.txt")));
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(files.Archive,
            context.CancellationToken));
    }

    [TestMethod]
    public async Task ReadRejectsExistingMetadataWithoutOverwritingIt()
    {
        using var files = new ArchiveFiles();
        PackageArchiveFixture.Write(files.Archive, PackageArchiveFixture.Required());
        string existing = Path.Combine(files.Metadata, "package.json");
        File.WriteAllText(existing, "previous");

        Assert.Contains("fresh empty directory", (await RejectAsync(files)).Message);
        Assert.AreEqual("previous", File.ReadAllText(existing));
    }

    [TestMethod]
    public async Task ReadRequiresAnExistingMetadataDirectory()
    {
        using var files = new ArchiveFiles();
        PackageArchiveFixture.Write(files.Archive, PackageArchiveFixture.Required());
        Directory.Delete(files.Metadata);
        Assert.Contains("fresh empty directory", (await RejectAsync(files)).Message);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("not an archive")]
    public async Task ReadRejectsNonArchiveContent(string contents)
    {
        using var files = new ArchiveFiles();
        File.WriteAllText(files.Archive, contents);
        await RejectAsync(files);
    }

    [TestMethod]
    public async Task ReadRejectsDirectoryInPlaceOfArchive()
    {
        using var files = new ArchiveFiles();
        Directory.CreateDirectory(files.Archive);
        Assert.Contains("missing or not a file", (await RejectAsync(files)).Message);
    }

    private Task<InvalidDataException> RejectAsync(ArchiveFiles files) =>
        Assert.ThrowsAsync<InvalidDataException>(() =>
            NodePackageArchive.ReadAsync(files.Archive, files.Metadata, context.CancellationToken));

    private sealed class ArchiveFiles : IDisposable
    {
        internal string Root { get; } = Directory.CreateTempSubdirectory(
            "workflow-native-archive-").FullName;
        internal string Archive => Path.Combine(Root, "package.tgz");
        internal string Metadata => Path.Combine(Root, "metadata");

        internal ArchiveFiles() => Directory.CreateDirectory(Metadata);

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
