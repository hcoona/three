using System.Formats.Tar;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.CI;

namespace WorkflowDelivery.Tests.CI;

[TestClass]
[DoNotParallelize]
public sealed class NodePackageArchiveTests(TestContext context)
{
    [TestMethod]
    [DataRow("missing-manifest")]
    [DataRow("missing-payload")]
    [DataRow("duplicate-manifest")]
    [DataRow("duplicate-payload")]
    [DataRow("linked-manifest")]
    [DataRow("directory-payload")]
    [DataRow("empty-manifest")]
    [DataRow("empty-payload")]
    [DataRow("binding-gyp")]
    [DataRow("hooks")]
    [DataRow("hook-child")]
    public async Task ReadRejectsMissingAmbiguousOrUnsupportedRequiredMember(string defect)
    {
        using var files = new ArchiveFiles();
        var members = PackageArchiveFixture.Required().ToList();
        switch (defect)
        {
            case "missing-manifest": members.RemoveAt(0); break;
            case "missing-payload": members.RemoveAt(1); break;
            case "duplicate-manifest": members.Add(members[0]); break;
            case "duplicate-payload": members.Add(members[1]); break;
            case "linked-manifest": members[0] = members[0] with
                { Kind = TarEntryType.SymbolicLink }; break;
            case "directory-payload": members[1] = members[1] with
                { Kind = TarEntryType.Directory }; break;
            case "empty-manifest": members[0] = members[0] with { Bytes = [] }; break;
            case "empty-payload": members[1] = members[1] with { Bytes = [] }; break;
            case "binding-gyp": members.Add(new("package/binding.gyp", [1])); break;
            case "hooks": members.Add(new("package/.hooks", [], TarEntryType.Directory)); break;
            case "hook-child": members.Add(new("package/.hooks/install", [1])); break;
        }
        PackageArchiveFixture.Write(files.Archive, members);

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            NodePackageArchive.ReadAsync(files.Archive, files.Metadata, context.CancellationToken));

        Assert.IsNotEmpty(error.Message);
        Assert.IsFalse(File.Exists(Path.Combine(files.Metadata, "dist", "index.js")));
    }

    [TestMethod]
    [DataRow("compressed", "compressed")]
    [DataRow("manifest", "member")]
    [DataRow("payload", "member")]
    [DataRow("member", "member bounds")]
    [DataRow("count", "member bounds")]
    [DataRow("expanded", "expanded")]
    public async Task ReadEnforcesArchiveBounds(string bound, string diagnostic)
    {
        using var files = new ArchiveFiles();
        ArchiveMember[] required = PackageArchiveFixture.Required();
        IEnumerable<ArchiveMember> members = required;
        switch (bound)
        {
            case "compressed":
                using (var sparse = File.Create(files.Archive))
                    sparse.SetLength(NodePackageArchive.CompressedLimit + 1);
                break;
            case "manifest":
                required[0] = required[0] with
                    { Bytes = new byte[NodePackageArchive.ManifestLimit + 1] };
                break;
            case "payload":
                required[1] = required[1] with
                    { Bytes = new byte[NodePackageArchive.ManifestLimit + 1] };
                break;
            case "member":
                members = required.Append(new("package/large.bin",
                    new byte[NodePackageArchive.MemberLimit + 1]));
                break;
            case "count":
                members = required.Concat(Enumerable.Range(0,
                    NodePackageArchive.MemberCountLimit - 1)
                    .Select(index => new ArchiveMember("package/extra-" + index, [1])));
                break;
            case "expanded":
                byte[] block = new byte[NodePackageArchive.MemberLimit];
                members = required.Concat(Enumerable.Range(0, 4).Select(index =>
                    new ArchiveMember("package/expanded-" + index, block)));
                break;
        }
        if (bound != "compressed")
            PackageArchiveFixture.Write(files.Archive, members);

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            NodePackageArchive.ReadAsync(files.Archive, files.Metadata, context.CancellationToken));

        Assert.Contains(diagnostic, error.Message);
    }

    [TestMethod]
    [DataRow("Pax")]
    [DataRow("Ustar")]
    [DataRow("V7")]
    public async Task ReadAcceptsNativeFormatsAndConsumesRequiredStreams(string format)
    {
        using var files = new ArchiveFiles();
        ArchiveMember[] required = PackageArchiveFixture.Required();
        required[1] = required[1] with { Bytes = new byte[NodePackageArchive.ManifestLimit] };
        PackageArchiveFixture.Write(files.Archive, required.Reverse(),
            Enum.Parse<TarEntryFormat>(format));

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
    public async Task ReadAcceptsMemberAndManifestByteBoundaries()
    {
        using var files = new ArchiveFiles();
        ArchiveMember[] required = PackageArchiveFixture.Required();
        required[0] = required[0] with { Bytes = new byte[NodePackageArchive.ManifestLimit] };
        PackageArchiveFixture.Write(files.Archive,
            required.Append(new("package/large.bin", new byte[NodePackageArchive.MemberLimit])));

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
    public async Task ReadAcceptsTheMemberCountBoundary()
    {
        using var files = new ArchiveFiles();
        IEnumerable<ArchiveMember> members = PackageArchiveFixture.Required().Concat(
            Enumerable.Range(0, NodePackageArchive.MemberCountLimit - 2)
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
    [DataRow("nonempty")]
    [DataRow("missing")]
    [DataRow("empty-archive")]
    [DataRow("directory-archive")]
    [DataRow("not-gzip")]
    public async Task ReadRejectsInvalidInputWithoutOverwritingExistingMetadata(string defect)
    {
        using var files = new ArchiveFiles();
        PackageArchiveFixture.Write(files.Archive, PackageArchiveFixture.Required());
        string existing = Path.Combine(files.Metadata, "package.json");
        if (defect == "nonempty")
            File.WriteAllText(existing, "previous");
        if (defect == "missing")
            Directory.Delete(files.Metadata);
        if (defect == "empty-archive")
            File.WriteAllBytes(files.Archive, []);
        if (defect == "directory-archive")
        {
            File.Delete(files.Archive);
            Directory.CreateDirectory(files.Archive);
        }
        if (defect == "not-gzip")
            File.WriteAllText(files.Archive, "not an archive");

        await Assert.ThrowsAsync<InvalidDataException>(() => NodePackageArchive.ReadAsync(
            files.Archive, files.Metadata, context.CancellationToken));

        if (defect == "nonempty")
            Assert.AreEqual("previous", File.ReadAllText(existing));
    }

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
