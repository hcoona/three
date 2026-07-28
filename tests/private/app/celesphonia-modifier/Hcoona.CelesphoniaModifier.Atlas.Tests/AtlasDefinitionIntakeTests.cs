using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hcoona.CelesphoniaModifier.Atlas;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class AtlasDefinitionIntakeTests
{
    [Fact]
    public void AbsolutePathNormalizationPreservesDriveRoots()
    {
        string root = Path.GetPathRoot(Environment.CurrentDirectory)!;

        string normalized = AtlasDefinitionIntakeContracts.NormalizeAbsolutePath(root);

        Assert.True(StringComparer.OrdinalIgnoreCase.Equals(Path.GetFullPath(root), normalized));
        Assert.True(Path.IsPathFullyQualified(normalized));
        Assert.True(
            AtlasDefinitionIntakeContracts.ContainsPath(
                normalized,
                Path.Combine(normalized, "synthetic-child")));
        Assert.False(
            AtlasDefinitionIntakeContracts.ContainsPath(
                Path.Combine(normalized, "synthetic-child"),
                normalized));
    }

    [Fact]
    public async Task CompleteIntakeCreatesDeterministicVerifiedSnapshot()
    {
        await using DefinitionIntakeWorkspace workspace =
            await DefinitionIntakeWorkspace.CreateAsync();
        List<(string Path, FileMode Mode, FileAccess Access, FileShare Share)> sourceOpens = [];
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            openFile: (path, mode, access, share, options) =>
            {
                if (AtlasDefinitionIntakeContracts.ContainsPath(
                        workspace.DefinitionRoot,
                        path))
                {
                    sourceOpens.Add((path, mode, access, share));
                }

                return AtlasIoSeams.Default.OpenFile(path, mode, access, share, options);
            },
            deleteDirectory: (path, recursive) =>
            {
                Assert.True(
                    AtlasDefinitionIntakeContracts.PathEquals(path, workspace.IncompleteRoot));
                AtlasIoSeams.Default.DeleteDirectory(path, recursive);
            },
            moveDirectory: (source, destination) =>
            {
                Assert.True(
                    AtlasDefinitionIntakeContracts.PathEquals(
                        source,
                        workspace.IncompleteRoot));
                Assert.True(
                    AtlasDefinitionIntakeContracts.PathEquals(
                        destination,
                        workspace.FinalRoot));
                AtlasIoSeams.Default.MoveDirectory(source, destination);
            },
            createDirectory: path =>
            {
                Assert.True(
                    AtlasDefinitionIntakeContracts.ContainsPath(
                        workspace.IncompleteRoot,
                        path));
                AtlasIoSeams.Default.CreateDirectory(path);
            });

        await AtlasDefinitionIntake.RunAsync(
            workspace.RequestPath,
            io,
            TestContext.Current.CancellationToken);

        Assert.True(Directory.Exists(workspace.FinalRoot));
        Assert.False(Directory.Exists(workspace.IncompleteRoot));
        AtlasDefinitionCopyReceipt receipt =
            await AtlasDefinitionIntakeContracts.ReadReceiptAsync(
                workspace.FinalReceiptPath,
                AtlasIoSeams.Default,
                TestContext.Current.CancellationToken);
        Assert.Equal(
            ["definition-source-000001", "definition-source-000002"],
            receipt.Entries.Select(static entry => entry.SourceAlias));
        Assert.Equal(
            [
                "definitions/definition-source-000001.json",
                "definitions/definition-source-000002.js",
            ],
            receipt.Entries.Select(static entry => entry.DestinationRelativePath));
        Assert.All(
            sourceOpens,
            static item =>
            {
                Assert.Equal(FileMode.Open, item.Mode);
                Assert.Equal(FileAccess.Read, item.Access);
                Assert.Equal(FileShare.Read, item.Share);
            });
        foreach (AtlasDefinitionCopyReceiptEntry entry in receipt.Entries)
        {
            string path = Path.Combine(
                workspace.FinalRoot,
                entry.DestinationRelativePath.Replace('/', Path.DirectorySeparatorChar));
            byte[] bytes = await File.ReadAllBytesAsync(
                path,
                TestContext.Current.CancellationToken);
            Assert.Equal(bytes.LongLength, entry.Length);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(bytes)), entry.Sha256);
        }
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("save-field")]
    public async Task RequestParsingIsStrict(string mutation)
    {
        await using DefinitionIntakeWorkspace workspace =
            await DefinitionIntakeWorkspace.CreateAsync();
        string json = await File.ReadAllTextAsync(
            workspace.RequestPath,
            TestContext.Current.CancellationToken);
        json = mutation switch
        {
            "unknown" => json.Replace(
                "\"buildId\":13624401",
                "\"buildId\":13624401,\"unknown\":true",
                StringComparison.Ordinal),
            "duplicate" => json.Replace(
                "\"runId\":\"",
                "\"runId\":\"00000000000000000000000000000000\",\"runId\":\"",
                StringComparison.Ordinal),
            "missing" => json.Replace(
                "\"applicationId\":1786790,",
                string.Empty,
                StringComparison.Ordinal),
            "null" => json.Replace(
                "\"runId\":\"0123456789abcdef0123456789abcdef\"",
                "\"runId\":null",
                StringComparison.Ordinal),
            "save-field" => json.Replace(
                "\"buildId\":13624401",
                "\"buildId\":13624401,\"save\":\"forbidden\"",
                StringComparison.Ordinal),
            _ => throw new InvalidOperationException(),
        };
        await File.WriteAllTextAsync(
            workspace.RequestPath,
            json,
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AtlasRequestException>(
            () => AtlasDefinitionIntake.RunAsync(
                workspace.RequestPath,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task HistoricalDigestIsCheckedBeforeAuthorityParsing()
    {
        await using DefinitionIntakeWorkspace workspace =
            await DefinitionIntakeWorkspace.CreateAsync();
        await File.WriteAllTextAsync(
            workspace.HistoricalAuthorityPath,
            "{not-json",
            TestContext.Current.CancellationToken);

        AtlasSafetyException exception = await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDefinitionIntake.RunAsync(
                workspace.RequestPath,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Contains("digest", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("expectedHistoricalAuthoritySha256")]
    [InlineData("expectedHistoricalAuthorityRevision")]
    [InlineData("applicationId")]
    [InlineData("buildId")]
    public async Task RequestHistoricalBindingMismatchesAreRejected(string field)
    {
        await using DefinitionIntakeWorkspace workspace =
            await DefinitionIntakeWorkspace.CreateAsync();
        await workspace.MutateRequestAsync(
            request => request[field] = field.EndsWith(
                "Sha256",
                StringComparison.Ordinal)
                ? new string('0', 64)
                : 1);

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunAsync().AsTask());
    }

    [Fact]
    public async Task HistoricalIngressIgnoresSaveExecutableAndOutputFields()
    {
        await using DefinitionIntakeWorkspace workspace =
            await DefinitionIntakeWorkspace.CreateAsync();

        await AtlasDefinitionIntake.RunAsync(
            workspace.RequestPath,
            TestContext.Current.CancellationToken);

        Assert.True(File.Exists(workspace.FinalReceiptPath));
    }

    [Fact]
    public async Task HistoricalIngressIgnoresDuplicateInertFields()
    {
        await using DefinitionIntakeWorkspace workspace =
            await DefinitionIntakeWorkspace.CreateAsync();
        string authority = await File.ReadAllTextAsync(
            workspace.HistoricalAuthorityPath,
            TestContext.Current.CancellationToken);
        authority = authority.Replace(
            "\"saveRoots\":\"ignored\",",
            "\"saveRoots\":\"first\",\"saveRoots\":\"second\",",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(
            workspace.HistoricalAuthorityPath,
            authority,
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);
        await workspace.RefreshAuthorityDigestAsync();

        await workspace.RunAsync();
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("save-field")]
    public async Task ReceiptParsingIsStrict(string mutation)
    {
        await using DefinitionIntakeWorkspace workspace =
            await DefinitionIntakeWorkspace.CreateAsync();
        await workspace.RunAsync();
        string json = await File.ReadAllTextAsync(
            workspace.FinalReceiptPath,
            TestContext.Current.CancellationToken);
        json = mutation switch
        {
            "unknown" => json.Replace(
                "\"entries\":",
                "\"unknown\":true,\"entries\":",
                StringComparison.Ordinal),
            "duplicate" => json.Replace(
                "\"runId\":\"",
                "\"runId\":\"00000000000000000000000000000000\",\"runId\":\"",
                StringComparison.Ordinal),
            "missing" => json.Replace(
                "\"applicationId\":1786790,",
                string.Empty,
                StringComparison.Ordinal),
            "null" => json.Replace(
                "\"runId\":\"0123456789abcdef0123456789abcdef\"",
                "\"runId\":null",
                StringComparison.Ordinal),
            "save-field" => json.Replace(
                "\"entries\":",
                "\"save\":\"forbidden\",\"entries\":",
                StringComparison.Ordinal),
            _ => throw new InvalidOperationException(),
        };
        await File.WriteAllTextAsync(
            workspace.FinalReceiptPath,
            json,
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AtlasSafetyException>(() => workspace.RunAsync().AsTask());
    }

    [Fact]
    public async Task ReceiptValidationIsSemanticRatherThanExactBytes()
    {
        await using DefinitionIntakeWorkspace workspace =
            await DefinitionIntakeWorkspace.CreateAsync();
        await workspace.RunAsync();
        JsonObject receipt = await DefinitionIntakeWorkspace.ReadJsonObjectAsync(
            workspace.FinalReceiptPath);
        await File.WriteAllTextAsync(
            workspace.FinalReceiptPath,
            receipt.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);

        await workspace.RunAsync();
    }

    [Fact]
    public async Task ExclusionsOccurBeforeMetadataAndRecursion()
    {
        await using DefinitionIntakeWorkspace workspace =
            await DefinitionIntakeWorkspace.CreateAsync();
        HashSet<string> excluded = new(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(workspace.DefinitionRoot, "Game.exe"),
            Path.Combine(workspace.DefinitionRoot, "save"),
            Path.Combine(workspace.DefinitionRoot, "www", "save"),
        };
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            getAttributes: path =>
                excluded.Contains(path)
                    ? throw new InvalidOperationException("Excluded metadata was accessed.")
                    : AtlasIoSeams.Default.GetAttributes(path),
            enumerateFileSystemEntries: (path, option) =>
                excluded.Contains(path)
                    ? throw new InvalidOperationException("An excluded root was traversed.")
                    : AtlasIoSeams.Default.EnumerateFileSystemEntries(path, option));

        await AtlasDefinitionIntake.RunAsync(
            workspace.RequestPath,
            io,
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task DefinitionAndWorkspaceOverlapIsRejected()
    {
        await using DefinitionIntakeWorkspace workspace =
            await DefinitionIntakeWorkspace.CreateAsync();
        await workspace.MutateRequestAsync(
            request => request["definitionRoot"] = workspace.WorkspaceRoot);

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDefinitionIntake.RunAsync(
                workspace.RequestPath,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task TraversalPathEscapeIsRejected()
    {
        await using DefinitionIntakeWorkspace workspace =
            await DefinitionIntakeWorkspace.CreateAsync();
        string external = Path.Combine(workspace.Root, "external.json");
        await File.WriteAllTextAsync(
            external,
            "{}",
            TestContext.Current.CancellationToken);
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            enumerateFileSystemEntries: (path, option) =>
                AtlasDefinitionIntakeContracts.PathEquals(path, workspace.DefinitionRoot)
                    ? AtlasIoSeams.Default.EnumerateFileSystemEntries(path, option).Append(external)
                    : AtlasIoSeams.Default.EnumerateFileSystemEntries(path, option));

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDefinitionIntake.RunAsync(
                workspace.RequestPath,
                io,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [InlineData("definition")]
    [InlineData("workspace")]
    [InlineData("source")]
    [InlineData("incomplete")]
    [InlineData("final")]
    public async Task TargetedReparsePointsAreRejected(string target)
    {
        await using DefinitionIntakeWorkspace workspace =
            await DefinitionIntakeWorkspace.CreateAsync();
        string source = Path.Combine(workspace.DefinitionRoot, "content", "actor.json");
        string reparsePath = target switch
        {
            "definition" => workspace.DefinitionRoot,
            "workspace" => workspace.WorkspaceRoot,
            "source" => source,
            "incomplete" => workspace.IncompleteRoot,
            "final" => workspace.FinalRoot,
            _ => throw new InvalidOperationException(),
        };
        if (StringComparer.Ordinal.Equals(target, "incomplete")
            || StringComparer.Ordinal.Equals(target, "final"))
        {
            Directory.CreateDirectory(reparsePath);
        }
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            getAttributes: path =>
                AtlasDefinitionIntakeContracts.PathEquals(path, reparsePath)
                    ? AtlasIoSeams.Default.GetAttributes(path) | FileAttributes.ReparsePoint
                    : AtlasIoSeams.Default.GetAttributes(path));

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDefinitionIntake.RunAsync(
                workspace.RequestPath,
                io,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task CaseCollidingTraversalEntriesAreRejected()
    {
        await using DefinitionIntakeWorkspace workspace =
            await DefinitionIntakeWorkspace.CreateAsync();
        string content = Path.Combine(workspace.DefinitionRoot, "content");
        string actor = Path.Combine(content, "actor.json");
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            enumerateFileSystemEntries: (path, option) =>
                AtlasDefinitionIntakeContracts.PathEquals(path, content)
                    ? [actor, Path.Combine(content, "ACTOR.JSON")]
                    : AtlasIoSeams.Default.EnumerateFileSystemEntries(path, option));

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDefinitionIntake.RunAsync(
                workspace.RequestPath,
                io,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("wrong-rule")]
    public async Task TwoWayHistoricalReconciliationRejectsMismatch(string mismatch)
    {
        await using DefinitionIntakeWorkspace workspace =
            await DefinitionIntakeWorkspace.CreateAsync();
        if (StringComparer.Ordinal.Equals(mismatch, "missing"))
        {
            File.Delete(Path.Combine(workspace.DefinitionRoot, "content", "actor.json"));
        }
        else if (StringComparer.Ordinal.Equals(mismatch, "extra"))
        {
            await File.WriteAllTextAsync(
                Path.Combine(workspace.DefinitionRoot, "content", "extra.json"),
                "{}",
                TestContext.Current.CancellationToken);
        }
        else
        {
            await workspace.MutateAuthorityAsync(
                authority =>
                    authority["definitionGroups"]![0]!["selectionRule"] = "other/*.json");
        }

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDefinitionIntake.RunAsync(
                workspace.RequestPath,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [InlineData("during-copy")]
    [InlineData("post-copy")]
    public async Task SourceChangesAreDetected(string phase)
    {
        await using DefinitionIntakeWorkspace workspace =
            await DefinitionIntakeWorkspace.CreateAsync();
        string source = Path.Combine(workspace.DefinitionRoot, "content", "actor.json");
        DateTimeOffset actual = AtlasIoSeams.Default.GetLastWriteTimeUtc(source);
        int calls = 0;
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            getLastWriteTimeUtc: path =>
            {
                if (!AtlasDefinitionIntakeContracts.PathEquals(path, source))
                {
                    return AtlasIoSeams.Default.GetLastWriteTimeUtc(path);
                }

                calls++;
                int changedAt = StringComparer.Ordinal.Equals(phase, "during-copy") ? 3 : 4;
                return calls >= changedAt ? actual.AddSeconds(1) : actual;
            });

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDefinitionIntake.RunAsync(
                workspace.RequestPath,
                io,
                TestContext.Current.CancellationToken).AsTask());
        Assert.True(Directory.Exists(workspace.IncompleteRoot));
        Assert.False(Directory.Exists(workspace.FinalRoot));
    }

    [Fact]
    public async Task DestinationCorruptionIsDetected()
    {
        await using DefinitionIntakeWorkspace workspace =
            await DefinitionIntakeWorkspace.CreateAsync();
        bool corruptNextDestinationRead = true;
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            openFile: (path, mode, access, share, options) =>
            {
                if (corruptNextDestinationRead
                    && mode == FileMode.Open
                    && access == FileAccess.Read
                    && AtlasDefinitionIntakeContracts.ContainsPath(
                        workspace.IncompleteRoot,
                        path)
                    && !path.EndsWith(
                        "definition-copy-receipt.json",
                        StringComparison.OrdinalIgnoreCase))
                {
                    corruptNextDestinationRead = false;
                    return new MemoryStream("corrupt"u8.ToArray(), writable: false);
                }

                return AtlasIoSeams.Default.OpenFile(path, mode, access, share, options);
            });

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => AtlasDefinitionIntake.RunAsync(
                workspace.RequestPath,
                io,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task ValidFinalIsIdempotentWithoutSourceTraversal()
    {
        await using DefinitionIntakeWorkspace workspace =
            await DefinitionIntakeWorkspace.CreateAsync();
        await workspace.RunAsync();
        Directory.Delete(Path.Combine(workspace.DefinitionRoot, "content"), recursive: true);
        Directory.Delete(Path.Combine(workspace.DefinitionRoot, "scripts"), recursive: true);

        await workspace.RunAsync();

        Assert.True(File.Exists(workspace.FinalReceiptPath));
    }

    [Fact]
    public async Task ValidIncompleteIsPromoted()
    {
        await using DefinitionIntakeWorkspace workspace =
            await DefinitionIntakeWorkspace.CreateAsync();
        await workspace.RunAsync();
        Directory.Move(workspace.FinalRoot, workspace.IncompleteRoot);

        await workspace.RunAsync();

        Assert.True(Directory.Exists(workspace.FinalRoot));
        Assert.False(Directory.Exists(workspace.IncompleteRoot));
    }

    [Fact]
    public async Task IncompleteWithoutValidReceiptIsDeletedAndRestarted()
    {
        await using DefinitionIntakeWorkspace workspace =
            await DefinitionIntakeWorkspace.CreateAsync();
        Directory.CreateDirectory(workspace.IncompleteRoot);
        await File.WriteAllTextAsync(
            Path.Combine(workspace.IncompleteRoot, "partial.txt"),
            "partial",
            TestContext.Current.CancellationToken);

        await workspace.RunAsync();

        Assert.True(Directory.Exists(workspace.FinalRoot));
        Assert.False(File.Exists(Path.Combine(workspace.FinalRoot, "partial.txt")));
    }

    [Fact]
    public async Task BothRootsAreRefusedWithoutModification()
    {
        await using DefinitionIntakeWorkspace workspace =
            await DefinitionIntakeWorkspace.CreateAsync();
        await workspace.RunAsync();
        Directory.CreateDirectory(workspace.IncompleteRoot);

        await Assert.ThrowsAsync<AtlasSafetyException>(() => workspace.RunAsync().AsTask());

        Assert.True(Directory.Exists(workspace.FinalRoot));
        Assert.True(Directory.Exists(workspace.IncompleteRoot));
    }

    [Theory]
    [InlineData("receipt")]
    [InlineData("missing-file")]
    [InlineData("extra-file")]
    public async Task InvalidFinalIsRefused(string corruption)
    {
        await using DefinitionIntakeWorkspace workspace =
            await DefinitionIntakeWorkspace.CreateAsync();
        await workspace.RunAsync();
        if (StringComparer.Ordinal.Equals(corruption, "receipt"))
        {
            await workspace.MutateReceiptAsync(
                receipt => receipt["runId"] = new string('f', 32));
        }
        else if (StringComparer.Ordinal.Equals(corruption, "missing-file"))
        {
            File.Delete(
                Path.Combine(
                    workspace.FinalRoot,
                    "definitions",
                    "definition-source-000001.json"));
        }
        else
        {
            await File.WriteAllTextAsync(
                Path.Combine(workspace.FinalRoot, "extra.txt"),
                "extra",
                TestContext.Current.CancellationToken);
        }

        await Assert.ThrowsAsync<AtlasSafetyException>(() => workspace.RunAsync().AsTask());
        Assert.True(Directory.Exists(workspace.FinalRoot));
    }

    [Theory]
    [InlineData("historicalAuthoritySha256")]
    [InlineData("historicalAuthorityRevision")]
    [InlineData("applicationId")]
    [InlineData("buildId")]
    [InlineData("definitionRoot")]
    [InlineData("finalCopyRoot")]
    [InlineData("sourceAlias")]
    [InlineData("destinationRelativePath")]
    [InlineData("length")]
    [InlineData("sha256")]
    public async Task EveryReceiptBindingMismatchIsRejected(string field)
    {
        await using DefinitionIntakeWorkspace workspace =
            await DefinitionIntakeWorkspace.CreateAsync();
        await workspace.RunAsync();
        await workspace.MutateReceiptAsync(
            receipt =>
            {
                JsonObject entry = receipt["entries"]![0]!.AsObject();
                switch (field)
                {
                    case "historicalAuthoritySha256":
                        receipt[field] = new string('0', 64);
                        break;
                    case "historicalAuthorityRevision":
                    case "applicationId":
                    case "buildId":
                        receipt[field] = 1;
                        break;
                    case "definitionRoot":
                    case "finalCopyRoot":
                        receipt[field] = Path.Combine(workspace.Root, "wrong");
                        break;
                    case "sourceAlias":
                        entry[field] = "definition-source-999999";
                        break;
                    case "destinationRelativePath":
                        entry[field] = "definitions/definition-source-999999.json";
                        break;
                    case "length":
                        entry[field] = 999;
                        break;
                    case "sha256":
                        entry[field] = new string('0', 64);
                        break;
                    default:
                        throw new InvalidOperationException();
                }
            });

        await Assert.ThrowsAsync<AtlasSafetyException>(() => workspace.RunAsync().AsTask());
    }
}

internal sealed class DefinitionIntakeWorkspace : IAsyncDisposable
{
    private const string RunIdValue = "0123456789abcdef0123456789abcdef";

    private DefinitionIntakeWorkspace(string root)
    {
        Root = root;
        RepositoryRoot = Path.Combine(root, "repository");
        DefinitionRoot = Path.Combine(root, "synthetic-definition-root");
        WorkspaceRoot = Path.Combine(
            RepositoryRoot,
            "src",
            "private",
            "app",
            "celesphonia-modifier",
            ".private",
            "atlas-definition-intake",
            RunIdValue);
        IncompleteRoot = Path.Combine(WorkspaceRoot, "definition-snapshot.incomplete");
        FinalRoot = Path.Combine(WorkspaceRoot, "definition-snapshot");
        FinalReceiptPath = Path.Combine(FinalRoot, "definition-copy-receipt.json");
        RequestPath = Path.Combine(root, "definition-intake-request.json");
        HistoricalRequestPath =
            HistoricalAtlasDefinitionIngress.GetHistoricalRequestPath(RepositoryRoot);
        HistoricalAuthorityPath =
            HistoricalAtlasDefinitionIngress.GetHistoricalAuthorityPath(RepositoryRoot);
    }

    public string Root { get; }

    public string RepositoryRoot { get; }

    public string DefinitionRoot { get; }

    public string WorkspaceRoot { get; }

    public string IncompleteRoot { get; }

    public string FinalRoot { get; }

    public string FinalReceiptPath { get; }

    public string RequestPath { get; }

    public string HistoricalRequestPath { get; }

    public string HistoricalAuthorityPath { get; }

    public static async ValueTask<DefinitionIntakeWorkspace> CreateAsync()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "celesphonia-definition-intake-tests",
            Guid.NewGuid().ToString("N"));
        DefinitionIntakeWorkspace workspace = new(root);
        await workspace.InitializeAsync();
        return workspace;
    }

    public ValueTask RunAsync() =>
        AtlasDefinitionIntake.RunAsync(
            RequestPath,
            TestContext.Current.CancellationToken);

    public async Task MutateRequestAsync(Action<JsonObject> mutation)
    {
        JsonObject request = await ReadJsonObjectAsync(RequestPath);
        mutation(request);
        await WriteJsonObjectAsync(RequestPath, request);
    }

    public async Task MutateAuthorityAsync(Action<JsonObject> mutation)
    {
        JsonObject authority = await ReadJsonObjectAsync(HistoricalAuthorityPath);
        mutation(authority);
        await WriteJsonObjectAsync(HistoricalAuthorityPath, authority);
        await RefreshAuthorityDigestAsync();
    }

    public async Task RefreshAuthorityDigestAsync()
    {
        string sha256 = ComputeSha256(HistoricalAuthorityPath);
        JsonObject historicalRequest = await ReadJsonObjectAsync(HistoricalRequestPath);
        historicalRequest["expectedBaselineSha256"] = sha256;
        await WriteJsonObjectAsync(HistoricalRequestPath, historicalRequest);
        JsonObject request = await ReadJsonObjectAsync(RequestPath);
        request["expectedHistoricalAuthoritySha256"] = sha256;
        await WriteJsonObjectAsync(RequestPath, request);
    }

    public async Task MutateReceiptAsync(Action<JsonObject> mutation)
    {
        JsonObject receipt = await ReadJsonObjectAsync(FinalReceiptPath);
        mutation(receipt);
        await WriteJsonObjectAsync(FinalReceiptPath, receipt);
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(HistoricalRequestPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(HistoricalAuthorityPath)!);
        Directory.CreateDirectory(Path.Combine(DefinitionRoot, "content"));
        Directory.CreateDirectory(Path.Combine(DefinitionRoot, "scripts"));
        Directory.CreateDirectory(Path.Combine(DefinitionRoot, "save"));
        Directory.CreateDirectory(Path.Combine(DefinitionRoot, "www", "save"));
        await File.WriteAllTextAsync(
            Path.Combine(DefinitionRoot, "content", "actor.json"),
            "{\"actor\":1}",
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(DefinitionRoot, "content", "notes.txt"),
            "excluded",
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(DefinitionRoot, "scripts", "main.JS"),
            "console.log('synthetic');",
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(DefinitionRoot, "Game.exe"),
            "synthetic executable",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(DefinitionRoot, "save", "file1.rpgsave"),
            "synthetic save",
            TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(
            Path.Combine(DefinitionRoot, "www", "save", "global.rpgsave"),
            "synthetic save",
            TestContext.Current.CancellationToken);

        JsonObject authority = CreateAuthority();
        await WriteJsonObjectAsync(HistoricalAuthorityPath, authority);
        string authoritySha256 = ComputeSha256(HistoricalAuthorityPath);
        JsonObject historicalRequest = new()
        {
            ["schemaVersion"] = AtlasIntakeContracts.DiscoveryRequestSchemaVersion,
            ["expectedBaselineSha256"] = authoritySha256,
            ["expectedSteamAppId"] = AtlasIntakeContracts.ExactSteamAppId,
            ["expectedBuildId"] = AtlasIntakeContracts.ExactBuildId,
            ["definitionRoot"] = "historically-inert",
            ["gameExecutablePath"] = "historically-inert",
            ["saveRoots"] = "historically-inert",
            ["outputPath"] = "historically-inert",
        };
        await WriteJsonObjectAsync(HistoricalRequestPath, historicalRequest);
        JsonObject request = new()
        {
            ["schemaVersion"] = AtlasDefinitionIntakeContracts.RequestSchemaVersion,
            ["repositoryRoot"] = RepositoryRoot,
            ["runId"] = RunIdValue,
            ["definitionRoot"] = DefinitionRoot,
            ["expectedHistoricalAuthoritySha256"] = authoritySha256,
            ["expectedHistoricalAuthorityRevision"] = 3,
            ["applicationId"] = AtlasIntakeContracts.ExactSteamAppId,
            ["buildId"] = AtlasIntakeContracts.ExactBuildId,
        };
        await WriteJsonObjectAsync(RequestPath, request);
    }

    private static JsonObject CreateAuthority() => new()
    {
        ["schemaVersion"] = AtlasIntakeContracts.IntakeManifestSchemaVersion,
        ["surveyAlias"] = AtlasIntakeContracts.ExactSurveyAlias,
        ["manifestRevision"] = 3,
        ["saveRoots"] = "ignored",
        ["saveEntries"] = new JsonObject { ["ignored"] = true },
        ["gameExecutablePath"] = 42,
        ["outputPath"] = false,
        ["definitionGroups"] = new JsonArray(
            new JsonObject
            {
                ["groupId"] = "json-specific",
                ["selectionRule"] = "content/*.{json,JSON}",
                ["discoveredCount"] = 1,
                ["decision"] = AtlasIntakeContracts.IncludeDefinitionDecision,
                ["reasonCode"] = "ignored",
            },
            new JsonObject
            {
                ["groupId"] = "script-specific",
                ["selectionRule"] = "scripts/*.{js,JS}",
                ["discoveredCount"] = 1,
                ["decision"] = AtlasIntakeContracts.IncludeDefinitionDecision,
            },
            new JsonObject
            {
                ["groupId"] = "content-fallback",
                ["selectionRule"] = "content/*",
                ["discoveredCount"] = 1,
                ["decision"] = AtlasIntakeContracts.ExcludeDefinitionDecision,
            }),
        ["definitionEntries"] = new JsonArray(
            new JsonObject
            {
                ["sourceAlias"] = "definition-source-000001",
                ["relativePath"] = "content/actor.json",
                ["groupId"] = "json-specific",
                ["decision"] = AtlasIntakeContracts.IncludeDefinitionDecision,
                ["entryType"] = "historically-inert",
            },
            new JsonObject
            {
                ["sourceAlias"] = "definition-source-000002",
                ["relativePath"] = "scripts/main.JS",
                ["groupId"] = "script-specific",
                ["decision"] = AtlasIntakeContracts.IncludeDefinitionDecision,
            },
            new JsonObject
            {
                ["sourceAlias"] = "definition-source-000003",
                ["relativePath"] = "content/notes.txt",
                ["groupId"] = "content-fallback",
                ["decision"] = AtlasIntakeContracts.ExcludeDefinitionDecision,
            }),
    };

    private static string ComputeSha256(string path) =>
        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    internal static async Task<JsonObject> ReadJsonObjectAsync(string path)
    {
        await using FileStream stream = File.OpenRead(path);
        JsonNode? node = await JsonNode.ParseAsync(
            stream,
            cancellationToken: TestContext.Current.CancellationToken);
        return node?.AsObject() ?? throw new InvalidOperationException();
    }

    private static async Task WriteJsonObjectAsync(string path, JsonObject value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            value.ToJsonString(new JsonSerializerOptions { WriteIndented = false }),
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);
    }
}
