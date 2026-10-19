using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Tests.Repository;

// Contract tests for the npm package path the Node group and Release preparation run, with the
// mise-locked Node and PNPM and the production command builders, against the committed
// fixtures/npm-contract workspace. Every command is offline; installs use only the packed
// original. A pin change that alters a consumed fact fails here.
[TestClass]
[TestCategory("PnpmNativeContract")]
public sealed class NpmPackContractTests(TestContext context)
{
    private const string Product = "product";
    private const string Version = "1.2.3";
    private static readonly string[] MetadataKeys = ["name", "version", "type", "main",
        "devDependencies", "scripts", "engines.runtime"];
    private static readonly string[] PreparationKeys = ["name", "publishConfig.directory",
        "scripts"];
    private static readonly string[] SkipVerify = ["--config.verify-deps-before-run=false"];
    private static readonly string[] Archive = ["package.tgz"];
    private static readonly string[] PackedMembers = ["package/LICENSE", "package/dist/index.js",
        "package/dist/prepacked.txt", "package/package.json"];

    // Runtime readback: Node answers "v" plus the locked version, PNPM the plain locked one;
    // a different selection is rejected by the same check.
    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public async Task RuntimeReadbackMatchesTheLockedSelection()
    {
        using var tool = await PnpmNativeTool.CreateAsync(context.CancellationToken);
        NativeCommandResult node = await tool.RunAsync("node", RepositoryToolSelection.SourceRoot,
            ["--version"], context.CancellationToken);
        NativeCommandResult pnpm = await tool.RunAsync("pnpm", RepositoryToolSelection.SourceRoot,
            ["--version"], context.CancellationToken);
        PnpmNativeTool.AssertExited(node, true);
        PnpmNativeTool.AssertExited(pnpm, true);

        LockedToolSelection.RequireReadback("node", "v" + RepositoryToolSelection.Locked("node"),
            node.Stdout.Trim());
        LockedToolSelection.RequireReadback("pnpm", RepositoryToolSelection.Locked("pnpm"),
            pnpm.Stdout.Trim());
        Assert.ThrowsExactly<InvalidDataException>(() => LockedToolSelection.RequireReadback(
            "pnpm", RepositoryToolSelection.NextMajor(RepositoryToolSelection.Locked("pnpm")),
            pnpm.Stdout.Trim()));
    }

    // The CI build/test command runs the script without installing first: the flag disables
    // PNPM's verify-deps install, which otherwise reinstalls the stale (absent) dependencies.
    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    [DataRow(true)]
    [DataRow(false)]
    public async Task VerifyDependenciesFlagRunsScriptsWithoutInstalling(bool flag)
    {
        using var tool = await PnpmNativeTool.CreateAsync(context.CancellationToken);
        using var copy = new PnpmGraphContractTests.FixtureCopy("npm-contract");
        string product = Path.Combine(copy.Root, Product);

        NativeCommandResult build = await tool.RunAsync("pnpm", copy.Root, ["--dir", product,
            .. flag ? SkipVerify : [], "run", "build"],
            context.CancellationToken);

        PnpmNativeTool.AssertExited(build, true);
        Assert.IsTrue(File.Exists(Path.Combine(product, "dist", "index.js")));
        Assert.AreEqual(!flag, Directory.Exists(Path.Combine(product, "node_modules")),
            "Installation before the script");
        if (flag)
        {
            NativeCommandResult test = await tool.RunAsync("pnpm", copy.Root, ["--dir", product,
                "--config.verify-deps-before-run=false", "run", "test"],
                context.CancellationToken);
            PnpmNativeTool.AssertExited(test, true);
        }
    }

    // pack --out writes exactly the requested path (spaces and pack markers are not
    // expanded), runs prepack, injects the root license and contains the ESM payload that
    // NodePackageArchive requires; a failing lifecycle fails the pack.
    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public async Task PackWritesTheRequestedArchiveWithLifecycleAndLicense()
    {
        using var tool = await PnpmNativeTool.CreateAsync(context.CancellationToken);
        using var copy = new PnpmGraphContractTests.FixtureCopy("npm-contract");
        string output = Directory.CreateDirectory(Path.Combine(copy.Scratch, "pack output"))
            .FullName;
        string archive = Path.Combine(output, "package.tgz");

        NativeCommandResult early = await tool.RunAsync(NpmPackageOperations.Pack(copy.Root,
            Product, archive), context.CancellationToken);
        PnpmNativeTool.AssertExited(early, false);
        Assert.IsFalse(File.Exists(archive), "A failed prepack still produced an archive.");

        await BuildAsync(tool, copy);
        NativeCommandResult pack = await tool.RunAsync(NpmPackageOperations.Pack(copy.Root,
            Product, archive), context.CancellationToken);

        PnpmNativeTool.AssertExited(pack, true);
        CollectionAssert.AreEqual(Archive, Directory.GetFileSystemEntries(output)
            .Select(Path.GetFileName).ToArray());
        CollectionAssert.AreEquivalent(PackedMembers, Members(archive));
        Assert.AreEqual(File.ReadAllText(Path.Combine(copy.Root, "LICENSE")),
            Member(archive, "package/LICENSE"));
        string metadata = Directory.CreateDirectory(Path.Combine(copy.Scratch, "metadata"))
            .FullName;
        await NodePackageArchive.ReadAsync(archive, metadata, context.CancellationToken);
        (long size, string sha256) = await NpmPackageOperations.OriginalAsync(archive,
            context.CancellationToken);
        Assert.IsGreaterThan(0L, size);
        Assert.HasCount(64, sha256);
    }

    // Multi-field pkg get answers one flat object keyed by each requested dotted path; the
    // adopted recipe rejects engines.runtime through that key, and Release preparation reads
    // publishConfig.directory the same way. Without the field the key is absent.
    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    [DataRow(true)]
    [DataRow(false)]
    public async Task PackedMetadataUsesFlatDottedKeys(bool declared)
    {
        using var tool = await PnpmNativeTool.CreateAsync(context.CancellationToken);
        using var copy = new PnpmGraphContractTests.FixtureCopy("npm-contract");
        string manifest = Path.Combine(copy.Root, Product, "package.json");
        if (!declared)
        {
            var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(manifest))!
                .AsObject();
            document.Remove("engines");
            document.Remove("publishConfig");
            File.WriteAllText(manifest, document.ToJsonString());
        }
        NativeCommandResult source = await tool.RunAsync("pnpm", copy.Root, ["--dir",
            Path.Combine(copy.Root, Product), "pkg", "get", "--json", "name",
            "publishConfig.directory", "scripts"], context.CancellationToken);
        PnpmNativeTool.AssertExited(source, true);
        using (JsonDocument preparation = JsonDocument.Parse(source.Stdout))
        {
            CollectionAssert.AreEqual(declared ? PreparationKeys : PreparationKeys.Where(key =>
                key != "publishConfig.directory").ToArray(), preparation.RootElement
                .EnumerateObject().Select(field => field.Name).ToArray());
            if (declared)
                Assert.AreEqual(".", preparation.RootElement.GetProperty(
                    "publishConfig.directory").GetString());
        }
        string archive = await PackAsync(tool, copy);
        string directory = Path.Combine(copy.Scratch, "metadata");

        NativeCommandResult packed = await tool.RunAsync(await NpmPackageOperations
            .MetadataAsync(archive, directory, context.CancellationToken),
            context.CancellationToken);

        PnpmNativeTool.AssertExited(packed, true);
        using JsonDocument metadata = JsonDocument.Parse(packed.Stdout);
        CollectionAssert.AreEqual(declared ? MetadataKeys : MetadataKeys.Where(key =>
            key != "engines.runtime").ToArray(), metadata.RootElement.EnumerateObject()
            .Select(field => field.Name).ToArray());
        if (declared)
        {
            InvalidDataException error = Assert.ThrowsExactly<InvalidDataException>(() =>
                AdoptedNpmPackage.ValidateMetadata(packed.Stdout, Version));
            Assert.AreEqual("Unsupported npm runtime/native-build metadata.", error.Message);
        }
        else
            AdoptedNpmPackage.ValidateMetadata(packed.Stdout, Version);
    }

    // The isolated offline consumer installs the original archive and imports its ESM API; a
    // consumer without the install cannot import it, and a missing archive fails the install.
    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    [DataRow("original")]
    [DataRow("missing")]
    [DataRow("not-installed")]
    public async Task OfflineConsumerInstallsAndImportsTheOriginal(string variant)
    {
        using var tool = await PnpmNativeTool.CreateAsync(context.CancellationToken);
        using var copy = new PnpmGraphContractTests.FixtureCopy("npm-contract");
        string archive = await PackAsync(tool, copy);
        string directory = Directory.CreateDirectory(Path.Combine(copy.Scratch, "consumer"))
            .FullName;
        byte[] original = File.ReadAllBytes(archive);

        if (variant != "not-installed")
        {
            NativeCommandResult install = await tool.RunAsync(await NpmPackageOperations
                .InstallAsync(variant == "missing"
                    ? Path.Combine(copy.Scratch, "absent.tgz") : archive, directory,
                    context.CancellationToken), context.CancellationToken);
            PnpmNativeTool.AssertExited(install, variant == "original");
        }
        else
            Directory.CreateDirectory(Path.Combine(directory, "consumer"));
        NativeCommandResult import = await tool.RunAsync(AdoptedNpmPackage.Import(directory),
            context.CancellationToken);

        PnpmNativeTool.AssertExited(import, variant == "original");
        CollectionAssert.AreEqual(original, File.ReadAllBytes(archive));
    }

    private async Task BuildAsync(PnpmNativeTool tool,
        PnpmGraphContractTests.FixtureCopy copy)
    {
        NativeCommandResult build = await tool.RunAsync("pnpm", copy.Root, ["--dir",
            Path.Combine(copy.Root, Product), "--config.verify-deps-before-run=false", "run",
            "build"], context.CancellationToken);
        PnpmNativeTool.AssertExited(build, true);
    }

    private async Task<string> PackAsync(PnpmNativeTool tool,
        PnpmGraphContractTests.FixtureCopy copy)
    {
        await BuildAsync(tool, copy);
        string archive = Path.Combine(Directory.CreateDirectory(Path.Combine(copy.Scratch,
            "original")).FullName, "package.tgz");
        PnpmNativeTool.AssertExited(await tool.RunAsync(NpmPackageOperations.Pack(copy.Root,
            Product, archive), context.CancellationToken), true);
        return archive;
    }

    private static string[] Members(string archive)
    {
        using var gzip = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        var names = new List<string>();
        while (tar.GetNextEntry() is { } entry) names.Add(entry.Name);
        return [.. names];
    }

    private static string Member(string archive, string name)
    {
        using var gzip = new GZipStream(File.OpenRead(archive), CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        while (tar.GetNextEntry() is { } entry)
            if (entry.Name == name)
            {
                using var reader = new StreamReader(entry.DataStream!);
                return reader.ReadToEnd();
            }
        throw new InvalidDataException("Missing archive member " + name);
    }
}
