using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Hcoona.CelesphoniaModifier.Atlas;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class AtlasSnapshotSurveyTests
{
    [Fact]
    public async Task MixedMaximumCorpusIsClosedOrderedSourceBoundAndDoesNotAccessSaveRoot()
    {
        await using SnapshotSurveyWorkspace workspace =
            await SnapshotSurveyWorkspace.CreateMaximumAsync();
        IReadOnlyDictionary<string, byte[]> snapshotBefore =
            await workspace.ReadSnapshotFilesAsync();
        Directory.Delete(workspace.SaveRoot, recursive: true);

        await workspace.RunAsync(
            CreateLiveSourceThrowingIo(workspace.SaveRoot),
            AtlasSnapshotSurveyLimits.Default,
            AtlasSaveReaderLimits.Default,
            AtlasStructuralScannerLimits.Default,
            TestContext.Current.CancellationToken);

        AtlasSnapshotSurveyManifest manifest = await workspace.ReadManifestAsync();
        string[] expectedNames =
        [
            "global.rpgsave",
            "config.rpgsave",
            .. Enumerable.Range(1, 20).Select(static index => $"file{index}.rpgsave"),
        ];
        Assert.Equal(expectedNames, manifest.Documents.Select(
            static document => document.CopiedSaveRelativePath));
        Assert.Equal(
            [
                AtlasDocumentRole.GlobalSave,
                AtlasDocumentRole.ConfigSave,
                .. Enumerable.Repeat(AtlasDocumentRole.SlotSave, 20),
            ],
            manifest.Documents.Select(static document => document.DocumentRole));
        Assert.Equal(
            expectedNames.Select(static name => name + ".structural-scan.json"),
            manifest.Documents.Select(static document => document.ScanRelativePath));
        Assert.Equal(22, manifest.Totals.DocumentCount);
        Assert.Equal(
            manifest.Documents.Sum(static document => document.CopiedSourceByteLength),
            manifest.Totals.CopiedSourceBytes);
        Assert.Equal(
            manifest.Documents.Sum(static document => document.PersistedScanByteLength),
            manifest.Totals.CanonicalScanBytes);
        Assert.Equal(
            manifest.Documents.Sum(static document => document.Census.NodeOccurrences),
            manifest.Totals.NodeOccurrences);

        foreach (AtlasSnapshotSurveyDocument document in manifest.Documents)
        {
            byte[] sourceBytes = await File.ReadAllBytesAsync(
                Path.Combine(workspace.SnapshotRoot, document.CopiedSaveRelativePath),
                TestContext.Current.CancellationToken);
            AtlasSaveReadResult source = AtlasSaveReader.Read(
                sourceBytes,
                cancellationToken: TestContext.Current.CancellationToken);
            byte[] scanBytes = await File.ReadAllBytesAsync(
                Path.Combine(workspace.FinalRoot, document.ScanRelativePath),
                TestContext.Current.CancellationToken);
            AtlasStructuralScanResult scan = AtlasStructuralScanJson.Parse(
                scanBytes,
                source,
                document.DocumentRole,
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(document.Census, scan.Document.Census);
            Assert.Equal(
                Convert.ToHexStringLower(SHA256.HashData(sourceBytes)),
                document.CopiedSourceSha256);
            Assert.Equal(
                Convert.ToHexStringLower(SHA256.HashData(scanBytes)),
                document.PersistedScanSha256);
        }

        Assert.Equal(snapshotBefore, await workspace.ReadSnapshotFilesAsync());
        Assert.Equal(
            expectedNames.Append(AtlasSnapshotSurveyContracts.ManifestFileName)
                .Select(static name => name.EndsWith(
                        ".rpgsave",
                        StringComparison.Ordinal)
                    ? name + ".structural-scan.json"
                    : name)
                .Order(StringComparer.Ordinal),
            Directory.EnumerateFiles(workspace.FinalRoot)
                .Select(Path.GetFileName)
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task DifferentRunIdsProduceByteIdenticalCandidates()
    {
        await using SnapshotSurveyWorkspace workspace =
            await SnapshotSurveyWorkspace.CreateAsync(
                ("global.rpgsave", "{\"value\":1}"),
                ("config.rpgsave", "[true,false]"),
                ("file3.rpgsave", "{\"@c\":1,\"self\":{\"@r\":1}}"));

        await workspace.RunAsync();
        IReadOnlyDictionary<string, byte[]> first =
            await workspace.ReadFinalFilesAsync();
        await workspace.UseSurveyRunIdAsync(
            "22222222222222222222222222222222");
        await workspace.RunAsync();
        IReadOnlyDictionary<string, byte[]> second =
            await workspace.ReadFinalFilesAsync();

        Assert.Equal(first.Keys.Order(), second.Keys.Order());
        foreach ((string name, byte[] bytes) in first)
        {
            Assert.Equal(bytes, second[name]);
        }
    }

    [Fact]
    public async Task CanonicalManifestHasExactShapeRoundTripsAndExecutesSchema()
    {
        await using SnapshotSurveyWorkspace workspace =
            await SnapshotSurveyWorkspace.CreateAsync(
                ("config.rpgsave", "0"));
        await workspace.RunAsync();
        byte[] bytes = await File.ReadAllBytesAsync(
            workspace.FinalManifestPath,
            TestContext.Current.CancellationToken);
        string text = Encoding.UTF8.GetString(bytes);

        Assert.StartsWith(
            "{\"schemaVersion\":\"atlas-snapshot-survey/v1\",\"documents\":[{"
                + "\"copiedSaveRelativePath\":\"config.rpgsave\","
                + "\"documentRole\":\"config-save\","
                + "\"scanRelativePath\":\"config.rpgsave.structural-scan.json\","
                + "\"copiedSourceByteLength\":",
            text,
            StringComparison.Ordinal);
        Assert.EndsWith("}}\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain(workspace.RepositoryRoot, text, StringComparison.Ordinal);
        Assert.DoesNotContain(workspace.SurveyRunId, text, StringComparison.Ordinal);
        AtlasSnapshotSurveyManifest parsed = AtlasSnapshotSurveyManifestJson.Parse(
            bytes,
            cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(
            bytes,
            AtlasSnapshotSurveyManifestJson.Serialize(
                parsed,
                cancellationToken: TestContext.Current.CancellationToken));

        using JsonDocument schema = JsonDocument.Parse(
            await File.ReadAllBytesAsync(
                SnapshotSurveyWorkspace.GetSchemaPath("atlas-snapshot-survey.schema.json"),
                TestContext.Current.CancellationToken));
        using JsonDocument instance = JsonDocument.Parse(bytes);
        Assert.True(IsSchemaValid(schema.RootElement, instance.RootElement, schema.RootElement));

        JsonObject invalid = JsonNode.Parse(bytes)!.AsObject();
        invalid["documents"]!.AsArray()[0]!["documentRole"] = "slot-save";
        using JsonDocument invalidInstance = JsonDocument.Parse(invalid.ToJsonString());
        Assert.False(
            IsSchemaValid(schema.RootElement, invalidInstance.RootElement, schema.RootElement));

        using JsonDocument requestSchema = JsonDocument.Parse(
            await File.ReadAllBytesAsync(
                SnapshotSurveyWorkspace.GetSchemaPath(
                    "atlas-snapshot-survey-request.schema.json"),
                TestContext.Current.CancellationToken));
        using JsonDocument requestInstance = JsonDocument.Parse(
            await File.ReadAllBytesAsync(
                workspace.RequestPath,
                TestContext.Current.CancellationToken));
        Assert.True(
            IsSchemaValid(
                requestSchema.RootElement,
                requestInstance.RootElement,
                requestSchema.RootElement));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("run")]
    [InlineData("relative")]
    [InlineData("deep")]
    public async Task RequestParsingIsStrictAndBounded(string mutation)
    {
        await using SnapshotSurveyWorkspace workspace =
            await SnapshotSurveyWorkspace.CreateAsync(
                ("global.rpgsave", "0"));
        string json = await File.ReadAllTextAsync(
            workspace.RequestPath,
            TestContext.Current.CancellationToken);
        JsonObject request = JsonNode.Parse(json)!.AsObject();
        json = mutation switch
        {
            "unknown" => json[..^1] + ",\"unknown\":true}",
            "duplicate" => json.Replace(
                "\"runId\":",
                $"\"runId\":\"{workspace.SurveyRunId}\",\"runId\":",
                StringComparison.Ordinal),
            "missing" => Mutate(request, static value => value.Remove("repositoryRoot")),
            "null" => Mutate(request, static value => value["snapshotReceiptPath"] = null),
            "run" => Mutate(request, static value => value["runId"] = "ABC"),
            "relative" => Mutate(
                request,
                static value => value["snapshotReceiptPath"] = "relative"),
            "deep" => json[..^1]
                + ",\"x\":{\"a\":{\"b\":{\"c\":{\"d\":{\"e\":{\"f\":{\"g\":1}}}}}}}}",
            _ => throw new InvalidOperationException(),
        };
        await File.WriteAllTextAsync(
            workspace.RequestPath,
            json,
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AtlasRequestException>(
            () => workspace.RunAsync().AsTask());

        static string Mutate(JsonObject value, Action<JsonObject> mutationAction)
        {
            mutationAction(value);
            return value.ToJsonString();
        }
    }

    [Fact]
    public async Task RequestByteStringAndTokenLimitsAreEnforced()
    {
        await using SnapshotSurveyWorkspace workspace =
            await SnapshotSurveyWorkspace.CreateAsync(
                ("global.rpgsave", "0"));
        await File.WriteAllTextAsync(
            workspace.RequestPath,
            new string(' ', AtlasSnapshotSurveyContracts.MaximumRequestBytes + 1),
            TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<AtlasRequestException>(
            () => workspace.RunAsync().AsTask());

        JsonObject longString = new()
        {
            ["schemaVersion"] = AtlasSnapshotSurveyContracts.RequestSchemaVersion,
            ["repositoryRoot"] = new string(
                'a',
                AtlasSnapshotSurveyContracts.MaximumStringLength + 1),
            ["runId"] = workspace.SurveyRunId,
            ["snapshotReceiptPath"] = workspace.SnapshotReceiptPath,
        };
        await WriteObjectAsync(workspace.RequestPath, longString);
        await Assert.ThrowsAsync<AtlasRequestException>(
            () => workspace.RunAsync().AsTask());

        JsonObject manyTokens = new()
        {
            ["schemaVersion"] = AtlasSnapshotSurveyContracts.RequestSchemaVersion,
            ["repositoryRoot"] = workspace.RepositoryRoot,
            ["runId"] = workspace.SurveyRunId,
            ["snapshotReceiptPath"] = workspace.SnapshotReceiptPath,
        };
        for (int index = 0; index < AtlasSnapshotSurveyContracts.MaximumRequestTokens; index++)
        {
            manyTokens[$"x{index}"] = index;
        }

        await WriteObjectAsync(workspace.RequestPath, manyTokens);
        await Assert.ThrowsAsync<AtlasRequestException>(
            () => workspace.RunAsync().AsTask());
    }

    [Theory]
    [InlineData("bom")]
    [InlineData("whitespace")]
    [InlineData("duplicate")]
    [InlineData("order")]
    [InlineData("null")]
    [InlineData("role")]
    [InlineData("scan")]
    [InlineData("total")]
    [InlineData("long-number")]
    public async Task ManifestStrictParsingRejectsCanonicalAndSchemaMutations(string mutation)
    {
        await using SnapshotSurveyWorkspace workspace =
            await SnapshotSurveyWorkspace.CreateAsync(
                ("global.rpgsave", "0"));
        await workspace.RunAsync();
        byte[] canonical = await File.ReadAllBytesAsync(
            workspace.FinalManifestPath,
            TestContext.Current.CancellationToken);
        string text = Encoding.UTF8.GetString(canonical);
        byte[] invalid = mutation switch
        {
            "bom" => [0xEF, 0xBB, 0xBF, .. canonical],
            "whitespace" => Encoding.UTF8.GetBytes(text[..^1] + " \n"),
            "duplicate" => Encoding.UTF8.GetBytes(
                text.Replace(
                    "\"schemaVersion\":",
                    "\"schemaVersion\":\"atlas-snapshot-survey/v1\",\"schemaVersion\":",
                    StringComparison.Ordinal)),
            "order" => Encoding.UTF8.GetBytes(
                text.Replace(
                    "\"copiedSaveRelativePath\":\"global.rpgsave\","
                        + "\"documentRole\":\"global-save\"",
                    "\"documentRole\":\"global-save\","
                        + "\"copiedSaveRelativePath\":\"global.rpgsave\"",
                    StringComparison.Ordinal)),
            "null" => Encoding.UTF8.GetBytes(
                text.Replace(
                    "\"documentRole\":\"global-save\"",
                    "\"documentRole\":null",
                    StringComparison.Ordinal)),
            "role" => Encoding.UTF8.GetBytes(
                text.Replace("\"global-save\"", "\"slot-save\"", StringComparison.Ordinal)),
            "scan" => Encoding.UTF8.GetBytes(
                text.Replace(
                    "global.rpgsave.structural-scan.json",
                    "config.rpgsave.structural-scan.json",
                    StringComparison.Ordinal)),
            "total" => Encoding.UTF8.GetBytes(
                text.Replace(
                    "\"documentCount\":1",
                    "\"documentCount\":2",
                    StringComparison.Ordinal)),
            "long-number" => Encoding.UTF8.GetBytes(
                text.Replace(
                    "\"documentCount\":1",
                    "\"documentCount\":123456789012345678901",
                    StringComparison.Ordinal)),
            _ => throw new InvalidOperationException(),
        };

        Assert.Throws<AtlasSafetyException>(
            () => AtlasSnapshotSurveyManifestJson.Parse(
                invalid,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AggregateLimitsAndCheckedArithmeticRefuseWithoutPromotion()
    {
        await using SnapshotSurveyWorkspace workspace =
            await SnapshotSurveyWorkspace.CreateAsync(
                ("global.rpgsave", "{\"a\":1}"));

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunAsync(
                AtlasIoSeams.Default,
                AtlasSnapshotSurveyLimits.Default with { MaximumObservations = 1 },
                AtlasSaveReaderLimits.Default,
                AtlasStructuralScannerLimits.Default,
                TestContext.Current.CancellationToken).AsTask());
        Assert.False(Directory.Exists(workspace.FinalRoot));

        await workspace.DeleteSurveyWorkspaceAsync();
        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunAsync(
                AtlasIoSeams.Default,
                AtlasSnapshotSurveyLimits.Default with { MaximumCanonicalScanBytes = 1 },
                AtlasSaveReaderLimits.Default,
                AtlasStructuralScannerLimits.Default,
                TestContext.Current.CancellationToken).AsTask());
        Assert.False(Directory.Exists(workspace.FinalRoot));

        await workspace.DeleteSurveyWorkspaceAsync();
        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunAsync(
                AtlasIoSeams.Default,
                AtlasSnapshotSurveyLimits.Default with { MaximumManifestBytes = 100 },
                AtlasSaveReaderLimits.Default,
                AtlasStructuralScannerLimits.Default,
                TestContext.Current.CancellationToken).AsTask());
        Assert.False(Directory.Exists(workspace.FinalRoot));

        AtlasSnapshotSurveyDocument first = CreateAggregateDocument(long.MaxValue);
        AtlasSnapshotSurveyDocument second = CreateAggregateDocument(1);
        Assert.Throws<AtlasSafetyException>(
            () => AtlasSnapshotSurveyManifestJson.CreateTotals(
                [first, second],
                AtlasSnapshotSurveyLimits.Default,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReaderAndScannerRefusalsLeaveSnapshotUnchanged()
    {
        await using SnapshotSurveyWorkspace invalidReader =
            await SnapshotSurveyWorkspace.CreateRawAsync(
                ("global.rpgsave", "not-valid-base64"u8.ToArray()));
        IReadOnlyDictionary<string, byte[]> readerBefore =
            await invalidReader.ReadSnapshotFilesAsync();
        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => invalidReader.RunAsync().AsTask());
        Assert.Equal(readerBefore, await invalidReader.ReadSnapshotFilesAsync());
        Assert.False(Directory.Exists(invalidReader.FinalRoot));

        await using SnapshotSurveyWorkspace scannerLimit =
            await SnapshotSurveyWorkspace.CreateAsync(
                ("global.rpgsave", "{\"a\":1}"));
        IReadOnlyDictionary<string, byte[]> scannerBefore =
            await scannerLimit.ReadSnapshotFilesAsync();
        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => scannerLimit.RunAsync(
                AtlasIoSeams.Default,
                AtlasSnapshotSurveyLimits.Default,
                AtlasSaveReaderLimits.Default,
                AtlasStructuralScannerLimits.Default with { MaximumObservations = 1 },
                TestContext.Current.CancellationToken).AsTask());
        Assert.Equal(scannerBefore, await scannerLimit.ReadSnapshotFilesAsync());
        Assert.False(Directory.Exists(scannerLimit.FinalRoot));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("duplicate")]
    [InlineData("changed")]
    [InlineData("corrupt")]
    [InlineData("wrong-case")]
    [InlineData("reparse")]
    [InlineData("directory")]
    [InlineData("out-of-root")]
    public async Task InvalidSnapshotShapesAreRefusedWithoutSurveyMutation(string mutation)
    {
        await using SnapshotSurveyWorkspace workspace =
            await SnapshotSurveyWorkspace.CreateAsync(
                ("global.rpgsave", "0"));
        string source = Path.Combine(workspace.SnapshotRoot, "global.rpgsave");
        AtlasIoSeams io = AtlasIoSeams.Default;
        switch (mutation)
        {
            case "missing":
                File.Delete(source);
                break;
            case "extra":
                await File.WriteAllTextAsync(
                    Path.Combine(workspace.SnapshotRoot, "extra.bin"),
                    "extra",
                    TestContext.Current.CancellationToken);
                break;
            case "duplicate":
                {
                    JsonObject receipt = await ReadObjectAsync(workspace.SnapshotReceiptPath);
                    JsonArray entries = receipt["entries"]!.AsArray();
                    entries.Add(JsonNode.Parse(entries[0]!.ToJsonString()));
                    await WriteObjectAsync(workspace.SnapshotReceiptPath, receipt);
                    break;
                }
            case "changed":
                await File.AppendAllTextAsync(
                    source,
                    "changed",
                    TestContext.Current.CancellationToken);
                break;
            case "corrupt":
                await File.WriteAllTextAsync(
                    workspace.SnapshotReceiptPath,
                    "{}",
                    TestContext.Current.CancellationToken);
                break;
            case "wrong-case":
                RenameLeaf(workspace.SnapshotRoot, "global.rpgsave", "GLOBAL.RPGSAVE");
                break;
            case "reparse":
                io = AtlasTestSupport.CreateIo(
                    getAttributes: path =>
                        AtlasSaveSnapshotContracts.PathEquals(path, source)
                            ? AtlasIoSeams.Default.GetAttributes(path)
                                | FileAttributes.ReparsePoint
                            : AtlasIoSeams.Default.GetAttributes(path));
                break;
            case "directory":
                File.Delete(source);
                Directory.CreateDirectory(source);
                break;
            case "out-of-root":
                {
                    JsonObject request = await ReadObjectAsync(workspace.RequestPath);
                    request["snapshotReceiptPath"] = Path.Combine(
                        workspace.Root,
                        AtlasSaveSnapshotContracts.ReceiptFileName);
                    File.Copy(
                        workspace.SnapshotReceiptPath,
                        request["snapshotReceiptPath"]!.GetValue<string>(),
                        overwrite: false);
                    await WriteObjectAsync(workspace.RequestPath, request);
                    break;
                }
        }

        IReadOnlyDictionary<string, byte[]> before = await ReadExistingFilesAsync(
            workspace.SnapshotRoot);
        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunAsync(
                io,
                AtlasSnapshotSurveyLimits.Default,
                AtlasSaveReaderLimits.Default,
                AtlasStructuralScannerLimits.Default,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(before, await ReadExistingFilesAsync(workspace.SnapshotRoot));
        Assert.False(Directory.Exists(workspace.FinalRoot));
    }

    [Fact]
    public async Task RecoveryBranchesPromoteReuseCleanOrRefuseUnchanged()
    {
        await using SnapshotSurveyWorkspace workspace =
            await SnapshotSurveyWorkspace.CreateAsync(
                ("global.rpgsave", "0"));
        await workspace.RunAsync();
        IReadOnlyDictionary<string, byte[]> valid = await workspace.ReadFinalFilesAsync();
        await workspace.RunAsync();
        Assert.Equal(valid, await workspace.ReadFinalFilesAsync());

        Directory.Move(workspace.FinalRoot, workspace.IncompleteRoot);
        await workspace.RunAsync();
        Assert.True(Directory.Exists(workspace.FinalRoot));
        Assert.False(Directory.Exists(workspace.IncompleteRoot));

        Directory.Delete(workspace.FinalRoot, recursive: true);
        Directory.CreateDirectory(workspace.IncompleteRoot);
        string partial = Path.Combine(
            workspace.IncompleteRoot,
            "global.rpgsave.structural-scan.json");
        await File.WriteAllTextAsync(
            partial,
            "partial",
            TestContext.Current.CancellationToken);
        List<bool> recursiveFlags = [];
        AtlasIoSeams cleaningIo = AtlasTestSupport.CreateIo(
            deleteDirectory: (path, recursive) =>
            {
                recursiveFlags.Add(recursive);
                AtlasIoSeams.Default.DeleteDirectory(path, recursive);
            });
        await workspace.RunAsync(
            cleaningIo,
            AtlasSnapshotSurveyLimits.Default,
            AtlasSaveReaderLimits.Default,
            AtlasStructuralScannerLimits.Default,
            TestContext.Current.CancellationToken);
        Assert.Equal([false], recursiveFlags);
        Assert.True(Directory.Exists(workspace.FinalRoot));

        Directory.CreateDirectory(workspace.IncompleteRoot);
        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunAsync().AsTask());
        Assert.True(Directory.Exists(workspace.FinalRoot));
        Assert.True(Directory.Exists(workspace.IncompleteRoot));
    }

    [Theory]
    [InlineData("unexpected")]
    [InlineData("directory")]
    [InlineData("reparse")]
    [InlineData("wrong-case")]
    public async Task NoncleanableIncompleteStateIsRefusedWithoutDeletion(string kind)
    {
        await using SnapshotSurveyWorkspace workspace =
            await SnapshotSurveyWorkspace.CreateAsync(
                ("global.rpgsave", "0"));
        Directory.CreateDirectory(workspace.IncompleteRoot);
        string child = kind switch
        {
            "unexpected" => Path.Combine(workspace.IncompleteRoot, "unexpected.bin"),
            "wrong-case" => Path.Combine(
                workspace.IncompleteRoot,
                "GLOBAL.RPGSAVE.STRUCTURAL-SCAN.JSON"),
            _ => Path.Combine(
                workspace.IncompleteRoot,
                "global.rpgsave.structural-scan.json"),
        };
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
            () => workspace.RunAsync(
                io,
                AtlasSnapshotSurveyLimits.Default,
                AtlasSaveReaderLimits.Default,
                AtlasStructuralScannerLimits.Default,
                TestContext.Current.CancellationToken).AsTask());
        Assert.True(File.Exists(child) || Directory.Exists(child));
    }

    [Fact]
    public async Task CancellationBeforeReadingWritingCleanupAndPromotionNeverCreatesFinal()
    {
        await using SnapshotSurveyWorkspace workspace =
            await SnapshotSurveyWorkspace.CreateAsync(
                ("global.rpgsave", "{\"a\":1}"));
        using CancellationTokenSource before = new();
        await before.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => workspace.RunAsync(
                AtlasIoSeams.Default,
                AtlasSnapshotSurveyLimits.Default,
                AtlasSaveReaderLimits.Default,
                AtlasStructuralScannerLimits.Default,
                before.Token).AsTask());
        Assert.False(Directory.Exists(workspace.FinalRoot));

        await workspace.DeleteSurveyWorkspaceAsync();
        using CancellationTokenSource writing = new();
        AtlasIoSeams writingIo = AtlasTestSupport.CreateIo(
            openFile: (path, mode, access, share, options) =>
                mode == FileMode.CreateNew
                    ? new CancelingWriteStream(writing)
                    : AtlasIoSeams.Default.OpenFile(path, mode, access, share, options));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => workspace.RunAsync(
                writingIo,
                AtlasSnapshotSurveyLimits.Default,
                AtlasSaveReaderLimits.Default,
                AtlasStructuralScannerLimits.Default,
                writing.Token).AsTask());
        Assert.False(Directory.Exists(workspace.FinalRoot));

        await workspace.DeleteSurveyWorkspaceAsync();
        Directory.CreateDirectory(workspace.IncompleteRoot);
        await File.WriteAllTextAsync(
            Path.Combine(
                workspace.IncompleteRoot,
                "global.rpgsave.structural-scan.json"),
            "partial",
            TestContext.Current.CancellationToken);
        using CancellationTokenSource cleanup = new();
        AtlasIoSeams cleanupIo = AtlasTestSupport.CreateIo(
            deleteFile: path =>
            {
                AtlasIoSeams.Default.DeleteFile(path);
                cleanup.Cancel();
            });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => workspace.RunAsync(
                cleanupIo,
                AtlasSnapshotSurveyLimits.Default,
                AtlasSaveReaderLimits.Default,
                AtlasStructuralScannerLimits.Default,
                cleanup.Token).AsTask());
        Assert.False(Directory.Exists(workspace.FinalRoot));

        await workspace.DeleteSurveyWorkspaceAsync();
        using CancellationTokenSource promotion = new();
        AtlasIoSeams promotionIo = AtlasTestSupport.CreateIo(
            moveDirectory: (_, _) =>
            {
                promotion.Cancel();
                throw new OperationCanceledException(promotion.Token);
            });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => workspace.RunAsync(
                promotionIo,
                AtlasSnapshotSurveyLimits.Default,
                AtlasSaveReaderLimits.Default,
                AtlasStructuralScannerLimits.Default,
                promotion.Token).AsTask());
        Assert.False(Directory.Exists(workspace.FinalRoot));
        Assert.True(Directory.Exists(workspace.IncompleteRoot));
    }

    private static AtlasSnapshotSurveyDocument CreateAggregateDocument(long sourceLength) =>
        new(
            "global.rpgsave",
            AtlasDocumentRole.GlobalSave,
            "global.rpgsave.structural-scan.json",
            sourceLength,
            new string('0', 64),
            1,
            new string('1', 64),
            new AtlasStructuralScanCensus(1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0));

    private static async Task<JsonObject> ReadObjectAsync(string path)
    {
        await using FileStream stream = File.OpenRead(path);
        return (await JsonNode.ParseAsync(
            stream,
            cancellationToken: TestContext.Current.CancellationToken))!.AsObject();
    }

    private static Task WriteObjectAsync(string path, JsonObject value) =>
        File.WriteAllTextAsync(
            path,
            value.ToJsonString(),
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);

    private static void RenameLeaf(string root, string oldLeaf, string newLeaf)
    {
        string intermediate = Path.Combine(root, Guid.NewGuid().ToString("N"));
        File.Move(Path.Combine(root, oldLeaf), intermediate);
        File.Move(intermediate, Path.Combine(root, newLeaf));
    }

    private static async Task<IReadOnlyDictionary<string, byte[]>> ReadExistingFilesAsync(
        string root)
    {
        Dictionary<string, byte[]> files = new(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(root).Order(StringComparer.Ordinal))
        {
            files.Add(
                Path.GetFileName(path),
                await File.ReadAllBytesAsync(
                    path,
                    TestContext.Current.CancellationToken));
        }

        return files;
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

    private static bool IsSchemaValid(
        JsonElement schema,
        JsonElement instance,
        JsonElement rootSchema)
    {
        schema = ResolveSchema(schema, rootSchema);
        if (schema.TryGetProperty("const", out JsonElement constant)
            && !JsonElement.DeepEquals(constant, instance))
        {
            return false;
        }

        if (schema.TryGetProperty("enum", out JsonElement enumValues)
            && !enumValues.EnumerateArray().Any(value => JsonElement.DeepEquals(value, instance)))
        {
            return false;
        }

        if (schema.TryGetProperty("pattern", out JsonElement pattern)
            && instance.ValueKind == JsonValueKind.String
            && !Regex.IsMatch(instance.GetString()!, pattern.GetString()!))
        {
            return false;
        }

        if (schema.TryGetProperty("type", out JsonElement type))
        {
            string? expectedType = type.GetString();
            bool typeMatches = expectedType switch
            {
                "object" => instance.ValueKind == JsonValueKind.Object,
                "array" => instance.ValueKind == JsonValueKind.Array,
                "string" => instance.ValueKind == JsonValueKind.String,
                "integer" => instance.ValueKind == JsonValueKind.Number
                    && instance.TryGetInt64(out _),
                _ => true,
            };
            if (!typeMatches)
            {
                return false;
            }
        }

        if (instance.ValueKind == JsonValueKind.Object)
        {
            JsonElement properties = schema.TryGetProperty(
                "properties",
                out JsonElement propertySchemas)
                ? propertySchemas
                : default;
            if (schema.TryGetProperty("required", out JsonElement required)
                && required.EnumerateArray().Any(
                    name => !instance.TryGetProperty(name.GetString()!, out _)))
            {
                return false;
            }

            if (schema.TryGetProperty("additionalProperties", out JsonElement additional)
                && additional.ValueKind == JsonValueKind.False
                && instance.EnumerateObject().Any(
                    property => properties.ValueKind != JsonValueKind.Object
                        || !properties.TryGetProperty(property.Name, out _)))
            {
                return false;
            }

            if (properties.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in instance.EnumerateObject())
                {
                    if (properties.TryGetProperty(property.Name, out JsonElement propertySchema)
                        && !IsSchemaValid(propertySchema, property.Value, rootSchema))
                    {
                        return false;
                    }
                }
            }
        }

        if (instance.ValueKind == JsonValueKind.Array)
        {
            int length = instance.GetArrayLength();
            if (schema.TryGetProperty("minItems", out JsonElement minimum)
                && length < minimum.GetInt32()
                || schema.TryGetProperty("maxItems", out JsonElement maximum)
                    && length > maximum.GetInt32())
            {
                return false;
            }

            if (schema.TryGetProperty("items", out JsonElement items)
                && instance.EnumerateArray().Any(
                    value => !IsSchemaValid(items, value, rootSchema)))
            {
                return false;
            }
        }

        if (instance.ValueKind == JsonValueKind.Number
            && instance.TryGetInt64(out long number))
        {
            if (schema.TryGetProperty("minimum", out JsonElement minimum)
                && number < minimum.GetInt64()
                || schema.TryGetProperty("maximum", out JsonElement maximum)
                    && number > maximum.GetInt64())
            {
                return false;
            }
        }

        if (schema.TryGetProperty("allOf", out JsonElement allOf))
        {
            foreach (JsonElement condition in allOf.EnumerateArray())
            {
                if (!condition.TryGetProperty("if", out JsonElement ifSchema)
                    || !IsSchemaValid(ifSchema, instance, rootSchema))
                {
                    continue;
                }

                if (condition.TryGetProperty("then", out JsonElement thenSchema)
                    && !IsSchemaValid(thenSchema, instance, rootSchema))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static JsonElement ResolveSchema(
        JsonElement schema,
        JsonElement rootSchema)
    {
        while (schema.TryGetProperty("$ref", out JsonElement reference))
        {
            string[] segments = reference.GetString()![2..].Split('/');
            schema = rootSchema;
            foreach (string segment in segments)
            {
                schema = schema.GetProperty(segment);
            }
        }

        return schema;
    }

    private sealed class CancelingWriteStream(CancellationTokenSource source) : MemoryStream
    {
        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            source.Cancel();
            return ValueTask.FromException(new OperationCanceledException(cancellationToken));
        }
    }
}

internal sealed class SnapshotSurveyWorkspace : IAsyncDisposable
{
    public const string SnapshotRunId = "11111111111111111111111111111111";
    public const string DefaultSurveyRunId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    private SnapshotSurveyWorkspace(string root)
    {
        Root = root;
        RepositoryRoot = Path.Combine(root, "repository");
        SaveRoot = Path.Combine(root, "synthetic-save");
        SnapshotRequestPath = Path.Combine(root, "snapshot-request.json");
        RequestPath = Path.Combine(root, "survey-request.json");
        SnapshotRoot = Path.Combine(
            RepositoryRoot,
            "src",
            "private",
            "app",
            "celesphonia-modifier",
            ".private",
            "atlas-save-snapshot",
            SnapshotRunId,
            "save-snapshot");
        SnapshotReceiptPath = Path.Combine(
            SnapshotRoot,
            AtlasSaveSnapshotContracts.ReceiptFileName);
        SurveyRunId = DefaultSurveyRunId;
        SetSurveyPaths();
    }

    public string Root { get; }

    public string RepositoryRoot { get; }

    public string SaveRoot { get; }

    public string SnapshotRequestPath { get; }

    public string RequestPath { get; }

    public string SnapshotRoot { get; }

    public string SnapshotReceiptPath { get; }

    public string SurveyRunId { get; private set; }

    public string WorkspaceRoot { get; private set; } = string.Empty;

    public string IncompleteRoot { get; private set; } = string.Empty;

    public string FinalRoot { get; private set; } = string.Empty;

    public string FinalManifestPath => Path.Combine(
        FinalRoot,
        AtlasSnapshotSurveyContracts.ManifestFileName);

    public static ValueTask<SnapshotSurveyWorkspace> CreateAsync(
        params (string Name, string Json)[] files) =>
        CreateRawAsync(
            files.Select(
                    file => (
                        file.Name,
                        AtlasLzStringCodec.CompressToBase64(
                            file.Json,
                            cancellationToken: TestContext.Current.CancellationToken)))
                .ToArray());

    public static ValueTask<SnapshotSurveyWorkspace> CreateMaximumAsync()
    {
        List<(string Name, string Json)> files =
        [
            ("global.rpgsave", "{\"kind\":\"global\",\"values\":[1,2]}"),
            ("config.rpgsave", "{\"kind\":\"config\",\"enabled\":true}"),
        ];
        files.AddRange(
            Enumerable.Range(1, 20)
                .Select(index => (
                    $"file{index}.rpgsave",
                    $"{{\"slot\":{index},\"value\":null}}")));
        return CreateAsync([.. files]);
    }

    public static async ValueTask<SnapshotSurveyWorkspace> CreateRawAsync(
        params (string Name, byte[] Bytes)[] files)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "celesphonia-snapshot-survey-tests",
            Guid.NewGuid().ToString("N"));
        SnapshotSurveyWorkspace workspace = new(root);
        Directory.CreateDirectory(
            Path.Combine(
                workspace.RepositoryRoot,
                "src",
                "private",
                "app",
                "celesphonia-modifier"));
        Directory.CreateDirectory(workspace.SaveRoot);
        foreach ((string name, byte[] bytes) in files)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(workspace.SaveRoot, name),
                bytes,
                TestContext.Current.CancellationToken);
        }

        await workspace.WriteSnapshotRequestAsync();
        await AtlasSaveSnapshot.RunAsync(
            workspace.SnapshotRequestPath,
            TestContext.Current.CancellationToken);
        await workspace.WriteSurveyRequestAsync();
        return workspace;
    }

    public ValueTask RunAsync() =>
        AtlasSnapshotSurvey.RunAsync(
            RequestPath,
            TestContext.Current.CancellationToken);

    public ValueTask RunAsync(
        AtlasIoSeams io,
        AtlasSnapshotSurveyLimits surveyLimits,
        AtlasSaveReaderLimits readerLimits,
        AtlasStructuralScannerLimits scannerLimits,
        CancellationToken cancellationToken) =>
        AtlasSnapshotSurvey.RunAsync(
            RequestPath,
            io,
            surveyLimits,
            readerLimits,
            scannerLimits,
            cancellationToken);

    public async Task<AtlasSnapshotSurveyManifest> ReadManifestAsync()
    {
        byte[] bytes = await File.ReadAllBytesAsync(
            FinalManifestPath,
            TestContext.Current.CancellationToken);
        return AtlasSnapshotSurveyManifestJson.Parse(
            bytes,
            cancellationToken: TestContext.Current.CancellationToken);
    }

    public Task<IReadOnlyDictionary<string, byte[]>> ReadSnapshotFilesAsync() =>
        ReadFilesAsync(SnapshotRoot);

    public Task<IReadOnlyDictionary<string, byte[]>> ReadFinalFilesAsync() =>
        ReadFilesAsync(FinalRoot);

    public async Task UseSurveyRunIdAsync(string runId)
    {
        SurveyRunId = runId;
        SetSurveyPaths();
        await WriteSurveyRequestAsync();
    }

    public Task DeleteSurveyWorkspaceAsync()
    {
        if (Directory.Exists(WorkspaceRoot))
        {
            Directory.Delete(WorkspaceRoot, recursive: true);
        }

        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    public static string GetSchemaPath(string schemaName)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "dirs.proj")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(
            directory?.FullName ?? throw new InvalidOperationException(),
            "src",
            "private",
            "app",
            "celesphonia-modifier",
            "docs",
            ".copilot",
            "schemas",
            "atlas-v0",
            schemaName);
    }

    private void SetSurveyPaths()
    {
        WorkspaceRoot = Path.Combine(
            RepositoryRoot,
            "src",
            "private",
            "app",
            "celesphonia-modifier",
            ".private",
            "atlas-snapshot-survey",
            SurveyRunId);
        IncompleteRoot = Path.Combine(WorkspaceRoot, "survey.incomplete");
        FinalRoot = Path.Combine(WorkspaceRoot, "survey");
    }

    private async Task WriteSnapshotRequestAsync()
    {
        JsonObject request = new()
        {
            ["schemaVersion"] = AtlasSaveSnapshotContracts.RequestSchemaVersion,
            ["repositoryRoot"] = RepositoryRoot,
            ["runId"] = SnapshotRunId,
            ["saveRoot"] = SaveRoot,
        };
        await File.WriteAllTextAsync(
            SnapshotRequestPath,
            request.ToJsonString(),
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);
    }

    private async Task WriteSurveyRequestAsync()
    {
        JsonObject request = new()
        {
            ["schemaVersion"] = AtlasSnapshotSurveyContracts.RequestSchemaVersion,
            ["repositoryRoot"] = RepositoryRoot,
            ["runId"] = SurveyRunId,
            ["snapshotReceiptPath"] = SnapshotReceiptPath,
        };
        await File.WriteAllTextAsync(
            RequestPath,
            request.ToJsonString(),
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);
    }

    private static async Task<IReadOnlyDictionary<string, byte[]>> ReadFilesAsync(
        string directory)
    {
        Dictionary<string, byte[]> result = new(StringComparer.Ordinal);
        foreach (string path in Directory.EnumerateFiles(directory).Order(StringComparer.Ordinal))
        {
            result.Add(
                Path.GetFileName(path),
                await File.ReadAllBytesAsync(
                    path,
                    TestContext.Current.CancellationToken));
        }

        return result;
    }
}
