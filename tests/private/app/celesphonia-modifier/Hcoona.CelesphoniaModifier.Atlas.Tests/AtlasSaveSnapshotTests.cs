using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hcoona.CelesphoniaModifier.Atlas;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class AtlasSaveSnapshotTests
{
    [Fact]
    public void SnapshotPathComparisonFollowsPlatformCaseSemantics()
    {
        string first = Path.Combine(
            Path.GetTempPath(),
            "atlas-path-case",
            "MixedCaseSegment");
        string second = Path.Combine(
            Path.GetTempPath(),
            "atlas-path-case",
            "mixedcasesegment");

        Assert.Equal(
            OperatingSystem.IsWindows(),
            AtlasSaveSnapshotContracts.PathEquals(first, second));
        Assert.Equal(
            OperatingSystem.IsWindows(),
            AtlasSaveSnapshotContracts.ContainsPath(
                first,
                Path.Combine(second, "child")));
    }

    [Fact]
    public void SnapshotCopyRequiresWindowsFileSharingSemantics()
    {
        AtlasSaveSnapshot.ValidateSnapshotCopyPlatform(isWindows: true);

        Assert.Throws<AtlasSafetyException>(
            () => AtlasSaveSnapshot.ValidateSnapshotCopyPlatform(isWindows: false));
    }

    [Fact]
    public async Task SnapshotCopiesSupportedImmediateChildrenInCanonicalOrder()
    {
        await using SaveSnapshotWorkspace workspace = await SaveSnapshotWorkspace.CreateAsync(
            ("FILE2.RPGSAVE", "slot-two"),
            ("global.RPGSAVE", "global"),
            ("file20.rpgsave", "slot-twenty"));
        Directory.CreateDirectory(Path.Combine(workspace.SaveRoot, "nested"));
        await File.WriteAllTextAsync(
            Path.Combine(workspace.SaveRoot, "ignored.bak"),
            "ignored",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(workspace.SaveRoot, "nested", "file1.rpgsave"),
            "nested",
            TestContext.Current.CancellationToken);
        List<(FileMode Mode, FileAccess Access, FileShare Share)> sourceOpens = [];
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            getAttributes: path =>
            {
                if (StringComparer.OrdinalIgnoreCase.Equals(
                        Path.GetFileName(path),
                        "ignored.bak")
                    || StringComparer.OrdinalIgnoreCase.Equals(
                        Path.GetFileName(path),
                        "nested"))
                {
                    throw new InvalidOperationException(
                        "Unsupported children must be ignored before metadata access.");
                }

                return AtlasIoSeams.Default.GetAttributes(path);
            },
            openFile: (path, mode, access, share, options) =>
            {
                if (AtlasSaveSnapshotContracts.ContainsPath(workspace.SaveRoot, path)
                    && !AtlasSaveSnapshotContracts.PathEquals(workspace.SaveRoot, path))
                {
                    sourceOpens.Add((mode, access, share));
                }

                return AtlasIoSeams.Default.OpenFile(path, mode, access, share, options);
            });

        await workspace.RunAsync(io, TestContext.Current.CancellationToken);

        AtlasSaveSnapshotReceipt receipt = await workspace.ReadReceiptAsync();
        Assert.Equal(
            ["global.RPGSAVE", "FILE2.RPGSAVE", "file20.rpgsave"],
            receipt.Entries.Select(static entry => entry.SourceFileName));
        Assert.Equal(
            ["global.rpgsave", "file2.rpgsave", "file20.rpgsave"],
            receipt.Entries.Select(static entry => entry.DestinationRelativePath));
        Assert.All(
            sourceOpens,
            static open =>
            {
                Assert.Equal(FileMode.Open, open.Mode);
                Assert.Equal(FileAccess.Read, open.Access);
                Assert.Equal(FileShare.Read, open.Share);
            });
        Assert.False(File.Exists(Path.Combine(workspace.FinalRoot, "ignored.bak")));
        Assert.False(Directory.Exists(Path.Combine(workspace.FinalRoot, "nested")));
        foreach (AtlasSaveSnapshotReceiptEntry entry in receipt.Entries)
        {
            byte[] bytes = await File.ReadAllBytesAsync(
                Path.Combine(workspace.FinalRoot, entry.DestinationRelativePath),
                TestContext.Current.CancellationToken);
            Assert.Equal(bytes.LongLength, entry.Length);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), entry.Sha256);
        }
    }

    [Fact]
    public async Task SnapshotNeverMutatesOriginals()
    {
        await using SaveSnapshotWorkspace workspace = await SaveSnapshotWorkspace.CreateAsync(
            ("config.rpgsave", "configuration"));
        string source = Path.Combine(workspace.SaveRoot, "config.rpgsave");
        byte[] beforeBytes = await File.ReadAllBytesAsync(
            source,
            TestContext.Current.CancellationToken);
        FileAttributes beforeAttributes = File.GetAttributes(source);
        DateTime beforeWrite = File.GetLastWriteTimeUtc(source);

        await workspace.RunAsync();

        Assert.Equal(
            beforeBytes,
            await File.ReadAllBytesAsync(source, TestContext.Current.CancellationToken));
        Assert.Equal(beforeAttributes, File.GetAttributes(source));
        Assert.Equal(beforeWrite, File.GetLastWriteTimeUtc(source));
    }

    [Fact]
    public async Task SparseSlotOnlySelectionIsValid()
    {
        await using SaveSnapshotWorkspace workspace = await SaveSnapshotWorkspace.CreateAsync(
            ("file7.rpgsave", "sparse"));

        await workspace.RunAsync();

        AtlasSaveSnapshotReceipt receipt = await workspace.ReadReceiptAsync();
        AtlasSaveSnapshotReceiptEntry entry = Assert.Single(receipt.Entries);
        Assert.Equal("file7.rpgsave", entry.DestinationRelativePath);
    }

    [Fact]
    public async Task NoSupportedFileIsRefused()
    {
        await using SaveSnapshotWorkspace workspace =
            await SaveSnapshotWorkspace.CreateAsync();
        await File.WriteAllTextAsync(
            Path.Combine(workspace.SaveRoot, "steam_autocloud.vdf"),
            "ignored",
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunAsync().AsTask());

        Assert.False(Directory.Exists(workspace.FinalRoot));
    }

    [Fact]
    public async Task SupportedCaseCollisionIsRefusedBeforeSecondMetadataRead()
    {
        await using SaveSnapshotWorkspace workspace = await SaveSnapshotWorkspace.CreateAsync(
            ("global.rpgsave", "global"));
        string source = Path.Combine(workspace.SaveRoot, "global.rpgsave");
        int sourceMetadataReads = 0;
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            enumerateFileSystemEntries: (path, option) =>
                AtlasSaveSnapshotContracts.PathEquals(path, workspace.SaveRoot)
                    ? [source, Path.Combine(workspace.SaveRoot, "GLOBAL.RPGSAVE")]
                    : AtlasIoSeams.Default.EnumerateFileSystemEntries(path, option),
            getAttributes: path =>
            {
                if (AtlasSaveSnapshotContracts.ContainsPath(workspace.SaveRoot, path))
                {
                    sourceMetadataReads++;
                }

                return AtlasIoSeams.Default.GetAttributes(path);
            });

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunAsync(io, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(2, sourceMetadataReads);
    }

    [Fact]
    public async Task SupportedDirectoryAndReparseAreRefused()
    {
        await using SaveSnapshotWorkspace workspace =
            await SaveSnapshotWorkspace.CreateAsync();
        string directory = Path.Combine(workspace.SaveRoot, "file1.rpgsave");
        Directory.CreateDirectory(directory);

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunAsync().AsTask());

        Directory.Delete(directory);
        await File.WriteAllTextAsync(
            directory,
            "synthetic",
            TestContext.Current.CancellationToken);
        AtlasIoSeams reparseIo = AtlasTestSupport.CreateIo(
            getAttributes: path =>
                AtlasSaveSnapshotContracts.PathEquals(path, directory)
                    ? AtlasIoSeams.Default.GetAttributes(path) | FileAttributes.ReparsePoint
                    : AtlasIoSeams.Default.GetAttributes(path));
        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunAsync(
                reparseIo,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task SaveOutputOverlapAndReparseRepositoryAreRefused()
    {
        await using SaveSnapshotWorkspace workspace = await SaveSnapshotWorkspace.CreateAsync(
            ("global.rpgsave", "global"));
        await workspace.MutateRequestAsync(
            request => request["saveRoot"] = workspace.WorkspaceRoot);
        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunAsync().AsTask());

        await workspace.MutateRequestAsync(
            request => request["saveRoot"] = workspace.SaveRoot);
        AtlasIoSeams reparseIo = AtlasTestSupport.CreateIo(
            getAttributes: path =>
                AtlasSaveSnapshotContracts.PathEquals(path, workspace.RepositoryRoot)
                    ? AtlasIoSeams.Default.GetAttributes(path) | FileAttributes.ReparsePoint
                    : AtlasIoSeams.Default.GetAttributes(path));
        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunAsync(
                reparseIo,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task SourceMetadataChangePreventsPromotion()
    {
        await using SaveSnapshotWorkspace workspace = await SaveSnapshotWorkspace.CreateAsync(
            ("global.rpgsave", new string('x', 4096)));
        string source = Path.Combine(workspace.SaveRoot, "global.rpgsave");
        int reads = 0;
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            getLastWriteTimeUtc: path =>
            {
                DateTimeOffset value = AtlasIoSeams.Default.GetLastWriteTimeUtc(path);
                return AtlasSaveSnapshotContracts.PathEquals(path, source) && ++reads >= 3
                    ? value.AddSeconds(1)
                    : value;
            });

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunAsync(io, TestContext.Current.CancellationToken).AsTask());

        Assert.False(Directory.Exists(workspace.FinalRoot));
    }

    [Fact]
    public async Task ValidFinalIsIdempotentWithoutLiveSourceAccess()
    {
        await using SaveSnapshotWorkspace workspace = await SaveSnapshotWorkspace.CreateAsync(
            ("global.rpgsave", "global"));
        await workspace.RunAsync();
        Directory.Delete(workspace.SaveRoot, recursive: true);
        AtlasIoSeams io = CreateLiveSourceThrowingIo(workspace.SaveRoot);

        await workspace.RunAsync(io, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(workspace.FinalReceiptPath));
    }

    [Fact]
    public async Task ValidIncompletePromotesWithoutLiveSourceAccess()
    {
        await using SaveSnapshotWorkspace workspace = await SaveSnapshotWorkspace.CreateAsync(
            ("file1.rpgsave", "slot"));
        await workspace.RunAsync();
        Directory.Move(workspace.FinalRoot, workspace.IncompleteRoot);
        Directory.Delete(workspace.SaveRoot, recursive: true);

        await workspace.RunAsync(
            CreateLiveSourceThrowingIo(workspace.SaveRoot),
            TestContext.Current.CancellationToken);

        Assert.True(Directory.Exists(workspace.FinalRoot));
        Assert.False(Directory.Exists(workspace.IncompleteRoot));
    }

    [Theory]
    [InlineData("copied-file")]
    [InlineData("receipt")]
    public async Task FinalCandidateRequiresCanonicalLowercaseLeaves(string kind)
    {
        await using SaveSnapshotWorkspace workspace = await SaveSnapshotWorkspace.CreateAsync(
            ("file1.rpgsave", "slot"));
        await workspace.RunAsync();
        string originalLeaf = kind == "receipt"
            ? AtlasSaveSnapshotContracts.ReceiptFileName
            : "file1.rpgsave";
        string changedLeaf = originalLeaf.ToUpperInvariant();
        RenameLeaf(workspace.FinalRoot, originalLeaf, changedLeaf);
        Directory.Delete(workspace.SaveRoot, recursive: true);

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunAsync(
                CreateLiveSourceThrowingIo(workspace.SaveRoot),
                TestContext.Current.CancellationToken).AsTask());

        Assert.True(Directory.Exists(workspace.FinalRoot));
        Assert.True(File.Exists(Path.Combine(workspace.FinalRoot, changedLeaf)));
    }

    [Theory]
    [InlineData("copied-file")]
    [InlineData("receipt")]
    public async Task IncompleteCandidateRequiresCanonicalLowercaseLeaves(string kind)
    {
        await using SaveSnapshotWorkspace workspace = await SaveSnapshotWorkspace.CreateAsync(
            ("file1.rpgsave", "slot"));
        await workspace.RunAsync();
        Directory.Move(workspace.FinalRoot, workspace.IncompleteRoot);
        string originalLeaf = kind == "receipt"
            ? AtlasSaveSnapshotContracts.ReceiptFileName
            : "file1.rpgsave";
        string changedLeaf = originalLeaf.ToUpperInvariant();
        RenameLeaf(workspace.IncompleteRoot, originalLeaf, changedLeaf);
        Directory.Delete(workspace.SaveRoot, recursive: true);

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunAsync(
                CreateLiveSourceThrowingIo(workspace.SaveRoot),
                TestContext.Current.CancellationToken).AsTask());

        Assert.False(Directory.Exists(workspace.FinalRoot));
        Assert.True(Directory.Exists(workspace.IncompleteRoot));
        Assert.True(File.Exists(Path.Combine(workspace.IncompleteRoot, changedLeaf)));
    }

    [Fact]
    public async Task BothRootsAndInvalidFinalAreRefusedUnchanged()
    {
        await using SaveSnapshotWorkspace workspace = await SaveSnapshotWorkspace.CreateAsync(
            ("global.rpgsave", "global"));
        await workspace.RunAsync();
        Directory.CreateDirectory(workspace.IncompleteRoot);
        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunAsync().AsTask());
        Assert.True(Directory.Exists(workspace.FinalRoot));
        Assert.True(Directory.Exists(workspace.IncompleteRoot));

        Directory.Delete(workspace.IncompleteRoot);
        await File.WriteAllTextAsync(
            Path.Combine(workspace.FinalRoot, "extra.bin"),
            "extra",
            TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunAsync().AsTask());
        Assert.True(File.Exists(Path.Combine(workspace.FinalRoot, "extra.bin")));
    }

    [Fact]
    public async Task InvalidCleanableIncompletePreflightsLiveSourceBeforeDeletion()
    {
        await using SaveSnapshotWorkspace workspace = await SaveSnapshotWorkspace.CreateAsync(
            ("global.rpgsave", "global"));
        Directory.CreateDirectory(workspace.IncompleteRoot);
        string ownedFile = Path.Combine(workspace.IncompleteRoot, "global.rpgsave");
        await File.WriteAllTextAsync(
            ownedFile,
            "partial",
            TestContext.Current.CancellationToken);
        Directory.Delete(workspace.SaveRoot, recursive: true);

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunAsync().AsTask());

        Assert.True(File.Exists(ownedFile));
    }

    [Fact]
    public async Task InvalidCleanableIncompleteIsDeletedIndividuallyAndRestarted()
    {
        await using SaveSnapshotWorkspace workspace = await SaveSnapshotWorkspace.CreateAsync(
            ("config.rpgsave", "config"));
        Directory.CreateDirectory(workspace.IncompleteRoot);
        string partial = Path.Combine(workspace.IncompleteRoot, "config.rpgsave");
        await File.WriteAllTextAsync(
            partial,
            "partial",
            TestContext.Current.CancellationToken);
        List<string> deletedFiles = [];
        List<bool> recursiveFlags = [];
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            deleteFile: path =>
            {
                deletedFiles.Add(path);
                AtlasIoSeams.Default.DeleteFile(path);
            },
            deleteDirectory: (path, recursive) =>
            {
                recursiveFlags.Add(recursive);
                AtlasIoSeams.Default.DeleteDirectory(path, recursive);
            });

        await workspace.RunAsync(io, TestContext.Current.CancellationToken);

        Assert.Equal([partial], deletedFiles);
        Assert.Equal([false], recursiveFlags);
        Assert.True(Directory.Exists(workspace.FinalRoot));
    }

    [Theory]
    [InlineData("unexpected-file")]
    [InlineData("directory")]
    [InlineData("reparse")]
    public async Task UnexpectedIncompleteChildRefusesWithoutCleanup(string kind)
    {
        await using SaveSnapshotWorkspace workspace = await SaveSnapshotWorkspace.CreateAsync(
            ("global.rpgsave", "global"));
        Directory.CreateDirectory(workspace.IncompleteRoot);
        string child = kind == "unexpected-file"
            ? Path.Combine(workspace.IncompleteRoot, "unexpected.bin")
            : Path.Combine(workspace.IncompleteRoot, "file1.rpgsave");
        if (kind == "directory")
        {
            Directory.CreateDirectory(child);
        }
        else
        {
            await File.WriteAllTextAsync(
                child,
                "partial",
                TestContext.Current.CancellationToken);
        }

        AtlasIoSeams io = kind == "reparse"
            ? AtlasTestSupport.CreateIo(
                getAttributes: path =>
                    AtlasSaveSnapshotContracts.PathEquals(path, child)
                        ? AtlasIoSeams.Default.GetAttributes(path)
                            | FileAttributes.ReparsePoint
                        : AtlasIoSeams.Default.GetAttributes(path))
            : AtlasIoSeams.Default;

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunAsync(io, TestContext.Current.CancellationToken).AsTask());

        Assert.True(File.Exists(child) || Directory.Exists(child));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("bad-run")]
    [InlineData("relative")]
    [InlineData("deep")]
    [InlineData("surrogate-property")]
    [InlineData("surrogate-value")]
    public async Task RequestParsingIsStrictAndBounded(string mutation)
    {
        await using SaveSnapshotWorkspace workspace = await SaveSnapshotWorkspace.CreateAsync(
            ("global.rpgsave", "global"));
        string json = await File.ReadAllTextAsync(
            workspace.RequestPath,
            TestContext.Current.CancellationToken);
        json = mutation switch
        {
            "unknown" => json.Replace(
                "\"saveRoot\":",
                "\"unknown\":true,\"saveRoot\":",
                StringComparison.Ordinal),
            "duplicate" => json.Replace(
                "\"runId\":",
                "\"runId\":\"00000000000000000000000000000000\",\"runId\":",
                StringComparison.Ordinal),
            "missing" => json.Replace(
                $"\"repositoryRoot\":{JsonSerializer.Serialize(workspace.RepositoryRoot)},",
                string.Empty,
                StringComparison.Ordinal),
            "null" => json.Replace(
                JsonSerializer.Serialize(workspace.SaveRoot),
                "null",
                StringComparison.Ordinal),
            "bad-run" => json.Replace(
                SaveSnapshotWorkspace.RunId,
                "ABC",
                StringComparison.Ordinal),
            "relative" => json.Replace(
                JsonSerializer.Serialize(workspace.SaveRoot),
                "\"relative\"",
                StringComparison.Ordinal),
            "deep" => json.Replace(
                "\"saveRoot\":",
                "\"x\":{\"a\":{\"b\":{\"c\":{\"d\":{\"e\":{\"f\":{\"g\":1}}}}}}},\"saveRoot\":",
                StringComparison.Ordinal),
            "surrogate-property" => json.Replace(
                "\"saveRoot\":",
                "\"\\uD800\":true,\"saveRoot\":",
                StringComparison.Ordinal),
            "surrogate-value" => json.Replace(
                JsonSerializer.Serialize(workspace.SaveRoot),
                "\"\\uD800\"",
                StringComparison.Ordinal),
            _ => throw new InvalidOperationException(),
        };
        await File.WriteAllTextAsync(
            workspace.RequestPath,
            json,
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AtlasRequestException>(
            () => workspace.RunAsync().AsTask());
    }

    [Fact]
    public async Task RequestByteStringNumberAndTokenLimitsAreEnforced()
    {
        await using SaveSnapshotWorkspace workspace = await SaveSnapshotWorkspace.CreateAsync(
            ("global.rpgsave", "global"));
        await File.WriteAllTextAsync(
            workspace.RequestPath,
            new string(' ', AtlasSaveSnapshotContracts.MaximumRequestBytes + 1),
            TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<AtlasRequestException>(
            () => workspace.RunAsync().AsTask());

        await workspace.WriteRequestAsync(
            new string('a', AtlasSaveSnapshotContracts.MaximumStringLength + 1));
        await Assert.ThrowsAsync<AtlasRequestException>(
            () => workspace.RunAsync().AsTask());
    }

    [Fact]
    public async Task ReceiptBindingsBytesAndExactTreeAreValidatedSemantically()
    {
        await using SaveSnapshotWorkspace workspace = await SaveSnapshotWorkspace.CreateAsync(
            ("file1.rpgsave", "slot"));
        await workspace.RunAsync();
        JsonObject receipt = await workspace.ReadReceiptObjectAsync();
        await File.WriteAllTextAsync(
            workspace.FinalReceiptPath,
            receipt.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            TestContext.Current.CancellationToken);
        Directory.Delete(workspace.SaveRoot, recursive: true);
        await workspace.RunAsync();

        receipt = await workspace.ReadReceiptObjectAsync();
        receipt["runId"] = "00000000000000000000000000000000";
        await File.WriteAllTextAsync(
            workspace.FinalReceiptPath,
            receipt.ToJsonString(),
            TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunAsync().AsTask());

        receipt["runId"] = SaveSnapshotWorkspace.RunId;
        await File.WriteAllTextAsync(
            workspace.FinalReceiptPath,
            receipt.ToJsonString(),
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(workspace.FinalRoot, "extra.bin"),
            "extra",
            TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunAsync().AsTask());
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("empty")]
    [InlineData("too-many")]
    [InlineData("negative")]
    [InlineData("long-number")]
    [InlineData("long-string")]
    [InlineData("deep")]
    [InlineData("schema")]
    [InlineData("run")]
    [InlineData("save-root")]
    [InlineData("final-root")]
    [InlineData("source")]
    [InlineData("destination")]
    [InlineData("hash")]
    public async Task ReceiptParsingAndEveryBindingAreStrict(string mutation)
    {
        await using SaveSnapshotWorkspace workspace = await SaveSnapshotWorkspace.CreateAsync(
            ("file1.rpgsave", "slot"));
        await workspace.RunAsync();
        string json = await File.ReadAllTextAsync(
            workspace.FinalReceiptPath,
            TestContext.Current.CancellationToken);
        JsonObject receipt = JsonNode.Parse(json)!.AsObject();
        JsonObject entry = receipt["entries"]!.AsArray()[0]!.AsObject();
        json = mutation switch
        {
            "unknown" => json[..^1] + ",\"unknown\":true}",
            "duplicate" => json.Replace(
                "\"runId\":",
                $"\"runId\":\"{SaveSnapshotWorkspace.RunId}\",\"runId\":",
                StringComparison.Ordinal),
            "missing" => Mutate(receipt, value => value.Remove("runId")),
            "null" => Mutate(receipt, value => value["saveRoot"] = null),
            "empty" => Mutate(receipt, value => value["entries"] = new JsonArray()),
            "too-many" => Mutate(
                receipt,
                value => value["entries"] = new JsonArray(
                    Enumerable.Range(0, 23)
                        .Select(_ => JsonNode.Parse(entry.ToJsonString()))
                        .ToArray())),
            "negative" => Mutate(entry, value => value["length"] = -1),
            "long-number" => json.Replace(
                "\"length\":4",
                "\"length\":123456789012345678901",
                StringComparison.Ordinal),
            "long-string" => Mutate(
                entry,
                value => value["sourceFileName"] = new string(
                    'a',
                    AtlasSaveSnapshotContracts.MaximumStringLength + 1)),
            "deep" => json[..^1] + ",\"x\":" + CreateDeepNode().ToJsonString() + "}",
            "schema" => Mutate(receipt, value => value["schemaVersion"] = "wrong"),
            "run" => Mutate(
                receipt,
                value => value["runId"] = "00000000000000000000000000000000"),
            "save-root" => Mutate(
                receipt,
                value => value["saveRoot"] = workspace.RepositoryRoot),
            "final-root" => Mutate(
                receipt,
                value => value["finalSnapshotRoot"] = workspace.WorkspaceRoot),
            "source" => Mutate(entry, value => value["sourceFileName"] = "file2.rpgsave"),
            "destination" => Mutate(
                entry,
                value => value["destinationRelativePath"] = "file2.rpgsave"),
            "hash" => Mutate(
                entry,
                value => value["sha256"] = new string('A', 64)),
            _ => throw new InvalidOperationException(),
        };
        await File.WriteAllTextAsync(
            workspace.FinalReceiptPath,
            json,
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);
        Directory.Delete(workspace.SaveRoot, recursive: true);

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunAsync(
                CreateLiveSourceThrowingIo(workspace.SaveRoot),
                TestContext.Current.CancellationToken).AsTask());

        Assert.True(Directory.Exists(workspace.FinalRoot));

        static string Mutate(JsonObject value, Action<JsonObject> mutation)
        {
            mutation(value);
            return value.ToJsonString();
        }

        static JsonNode CreateDeepNode()
        {
            JsonNode value = JsonValue.Create(1)!;
            for (int index = 0; index < 9; index++)
            {
                value = new JsonObject { ["x"] = value };
            }

            return value;
        }
    }

    [Fact]
    public async Task CancellationAndIoFailureNeverPromote()
    {
        await using SaveSnapshotWorkspace workspace = await SaveSnapshotWorkspace.CreateAsync(
            ("global.rpgsave", new string('x', 64 * 1024)));
        using CancellationTokenSource source = new();
        await source.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => workspace.RunAsync(AtlasIoSeams.Default, source.Token).AsTask());
        Assert.False(Directory.Exists(workspace.FinalRoot));

        AtlasIoSeams failingIo = AtlasTestSupport.CreateIo(
            openFile: (path, mode, access, share, options) =>
                mode == FileMode.CreateNew
                    ? throw new IOException("synthetic")
                    : AtlasIoSeams.Default.OpenFile(path, mode, access, share, options));
        await Assert.ThrowsAsync<IOException>(
            () => workspace.RunAsync(
                failingIo,
                TestContext.Current.CancellationToken).AsTask());
        Assert.False(Directory.Exists(workspace.FinalRoot));
    }

    [Fact]
    public async Task CancellationDuringSelectionIsNotMaskedAsMissingSupportedFiles()
    {
        await using SaveSnapshotWorkspace workspace =
            await SaveSnapshotWorkspace.CreateAsync();
        using CancellationTokenSource source = new();
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            enumerateFileSystemEntries: (path, searchOption) =>
                AtlasSaveSnapshotContracts.PathEquals(path, workspace.SaveRoot)
                    ? CancelAfterIgnoredEntry()
                    : AtlasIoSeams.Default.EnumerateFileSystemEntries(path, searchOption));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => workspace.RunAsync(io, source.Token).AsTask());

        IEnumerable<string> CancelAfterIgnoredEntry()
        {
            yield return Path.Combine(workspace.SaveRoot, "ignored.bak");
            source.Cancel();
        }
    }

    [Fact]
    public async Task CancellationAfterNewCandidateValidationPreventsPromotion()
    {
        await using SaveSnapshotWorkspace workspace = await SaveSnapshotWorkspace.CreateAsync(
            ("global.rpgsave", "global"));
        using CancellationTokenSource source = new();
        int incompleteLengthReads = 0;
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            getLength: path =>
            {
                long length = AtlasIoSeams.Default.GetLength(path);
                if (AtlasSaveSnapshotContracts.ContainsPath(workspace.IncompleteRoot, path)
                    && StringComparer.Ordinal.Equals(
                        Path.GetFileName(path),
                        "global.rpgsave")
                    && ++incompleteLengthReads == 2)
                {
                    source.Cancel();
                }

                return length;
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => workspace.RunAsync(io, source.Token).AsTask());

        Assert.True(Directory.Exists(workspace.IncompleteRoot));
        Assert.False(Directory.Exists(workspace.FinalRoot));
    }

    [Fact]
    public async Task CancellationAfterIncompleteValidationPreventsPromotion()
    {
        await using SaveSnapshotWorkspace workspace = await SaveSnapshotWorkspace.CreateAsync(
            ("global.rpgsave", "global"));
        await workspace.RunAsync();
        Directory.Move(workspace.FinalRoot, workspace.IncompleteRoot);
        using CancellationTokenSource source = new();
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            getLength: path =>
            {
                long length = AtlasIoSeams.Default.GetLength(path);
                if (AtlasSaveSnapshotContracts.ContainsPath(workspace.IncompleteRoot, path)
                    && StringComparer.Ordinal.Equals(
                        Path.GetFileName(path),
                        "global.rpgsave"))
                {
                    source.Cancel();
                }

                return length;
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => workspace.RunAsync(io, source.Token).AsTask());

        Assert.True(Directory.Exists(workspace.IncompleteRoot));
        Assert.False(Directory.Exists(workspace.FinalRoot));
    }

    private static AtlasIoSeams CreateLiveSourceThrowingIo(string saveRoot) =>
        AtlasTestSupport.CreateIo(
            fileExists: path =>
                AtlasSaveSnapshotContracts.ContainsPath(saveRoot, path)
                    ? throw new InvalidOperationException("Live source access is forbidden.")
                    : AtlasIoSeams.Default.FileExists(path),
            directoryExists: path =>
                AtlasSaveSnapshotContracts.ContainsPath(saveRoot, path)
                    ? throw new InvalidOperationException("Live source access is forbidden.")
                    : AtlasIoSeams.Default.DirectoryExists(path),
            getAttributes: path =>
                AtlasSaveSnapshotContracts.ContainsPath(saveRoot, path)
                    ? throw new InvalidOperationException("Live source access is forbidden.")
                    : AtlasIoSeams.Default.GetAttributes(path),
            enumerateFileSystemEntries: (path, option) =>
                AtlasSaveSnapshotContracts.ContainsPath(saveRoot, path)
                    ? throw new InvalidOperationException("Live source access is forbidden.")
                    : AtlasIoSeams.Default.EnumerateFileSystemEntries(path, option),
            openFile: (path, mode, access, share, options) =>
                AtlasSaveSnapshotContracts.ContainsPath(saveRoot, path)
                    ? throw new InvalidOperationException("Live source access is forbidden.")
                    : AtlasIoSeams.Default.OpenFile(path, mode, access, share, options));

    private static void RenameLeaf(string root, string oldLeaf, string newLeaf)
    {
        string original = Path.Combine(root, oldLeaf);
        string intermediate = Path.Combine(root, $"rename-{Guid.NewGuid():N}");
        File.Move(original, intermediate);
        File.Move(intermediate, Path.Combine(root, newLeaf));
    }
}

internal sealed class SaveSnapshotWorkspace : IAsyncDisposable
{
    public const string RunId = "0123456789abcdef0123456789abcdef";

    private SaveSnapshotWorkspace(string root)
    {
        Root = root;
        RepositoryRoot = Path.Combine(root, "repository");
        SaveRoot = Path.Combine(root, "live-save");
        RequestPath = Path.Combine(root, "request.json");
        WorkspaceRoot = Path.Combine(
            RepositoryRoot,
            "src",
            "private",
            "app",
            "celesphonia-modifier",
            ".private",
            "atlas-save-snapshot",
            RunId);
        IncompleteRoot = Path.Combine(WorkspaceRoot, "save-snapshot.incomplete");
        FinalRoot = Path.Combine(WorkspaceRoot, "save-snapshot");
        FinalReceiptPath = Path.Combine(
            FinalRoot,
            AtlasSaveSnapshotContracts.ReceiptFileName);
    }

    public string Root { get; }

    public string RepositoryRoot { get; }

    public string SaveRoot { get; }

    public string RequestPath { get; }

    public string WorkspaceRoot { get; }

    public string IncompleteRoot { get; }

    public string FinalRoot { get; }

    public string FinalReceiptPath { get; }

    public static async ValueTask<SaveSnapshotWorkspace> CreateAsync(
        params (string Name, string Content)[] files)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "celesphonia-save-snapshot-tests",
            Guid.NewGuid().ToString("N"));
        SaveSnapshotWorkspace workspace = new(root);
        Directory.CreateDirectory(
            Path.Combine(
                workspace.RepositoryRoot,
                "src",
                "private",
                "app",
                "celesphonia-modifier"));
        Directory.CreateDirectory(workspace.SaveRoot);
        foreach ((string name, string content) in files)
        {
            await File.WriteAllTextAsync(
                Path.Combine(workspace.SaveRoot, name),
                content,
                new UTF8Encoding(false),
                TestContext.Current.CancellationToken);
        }

        await workspace.WriteRequestAsync(workspace.SaveRoot);
        return workspace;
    }

    public ValueTask RunAsync() =>
        AtlasSaveSnapshot.RunAsync(
            RequestPath,
            AtlasIoSeams.Default,
            TestContext.Current.CancellationToken);

    public ValueTask RunAsync(AtlasIoSeams io) =>
        AtlasSaveSnapshot.RunAsync(
            RequestPath,
            io,
            TestContext.Current.CancellationToken);

    public ValueTask RunAsync(AtlasIoSeams io, CancellationToken cancellationToken) =>
        AtlasSaveSnapshot.RunAsync(RequestPath, io, cancellationToken);

    public async Task WriteRequestAsync(string saveRoot)
    {
        JsonObject request = new()
        {
            ["schemaVersion"] = AtlasSaveSnapshotContracts.RequestSchemaVersion,
            ["repositoryRoot"] = RepositoryRoot,
            ["runId"] = RunId,
            ["saveRoot"] = saveRoot,
        };
        await File.WriteAllTextAsync(
            RequestPath,
            request.ToJsonString(),
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);
    }

    public async Task MutateRequestAsync(Action<JsonObject> mutation)
    {
        JsonObject request = await ReadObjectAsync(RequestPath);
        mutation(request);
        await File.WriteAllTextAsync(
            RequestPath,
            request.ToJsonString(),
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);
    }

    public ValueTask<AtlasSaveSnapshotReceipt> ReadReceiptAsync() =>
        AtlasSaveSnapshotContracts.ReadReceiptAsync(
            FinalReceiptPath,
            AtlasIoSeams.Default,
            TestContext.Current.CancellationToken);

    public Task<JsonObject> ReadReceiptObjectAsync() => ReadObjectAsync(FinalReceiptPath);

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private static async Task<JsonObject> ReadObjectAsync(string path)
    {
        await using FileStream stream = File.OpenRead(path);
        JsonNode? value = await JsonNode.ParseAsync(
            stream,
            cancellationToken: TestContext.Current.CancellationToken);
        return value?.AsObject() ?? throw new InvalidOperationException();
    }
}
