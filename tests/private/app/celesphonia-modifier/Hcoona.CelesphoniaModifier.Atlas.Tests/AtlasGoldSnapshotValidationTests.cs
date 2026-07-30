using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hcoona.CelesphoniaModifier.Atlas;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class AtlasGoldSnapshotValidationTests
{
    public static TheoryData<
        string,
        string,
        AtlasGoldSnapshotValidationState,
        int,
        int,
        int> OverallStateCases =>
        new()
        {
            {
                "all-consistent",
                GoldJson("11", "11"),
                AtlasGoldSnapshotValidationState.AllConsistent,
                1,
                0,
                0
            },
            {
                "disagreement",
                GoldJson("11", "12"),
                AtlasGoldSnapshotValidationState.DisagreementObserved,
                0,
                1,
                0
            },
            {
                "incomplete",
                "{\"party\":{\"_gold\":11}}",
                AtlasGoldSnapshotValidationState.IncompleteObserved,
                0,
                0,
                1
            },
        };

    [Theory]
    [MemberData(nameof(OverallStateCases))]
    public async Task SingleSlotStatesAreDerivedAndDeterministic(
        string caseName,
        string json,
        AtlasGoldSnapshotValidationState expectedState,
        int expectedConsistent,
        int expectedDisagree,
        int expectedIncomplete)
    {
        _ = caseName;
        await using SnapshotSurveyWorkspace workspace =
            await CreateValidationWorkspaceAsync(("file1.rpgsave", json));

        AtlasGoldSnapshotValidationSummary first = await workspace.RunGoldValidationAsync();
        AtlasGoldSnapshotValidationSummary second = await workspace.RunGoldValidationAsync();

        AssertSummary(
            first,
            expectedState,
            total: 1,
            expectedConsistent,
            expectedDisagree,
            expectedIncomplete);
        AssertSummary(
            second,
            expectedState,
            total: 1,
            expectedConsistent,
            expectedDisagree,
            expectedIncomplete);
    }

    [Fact]
    public async Task DisagreementAndIncompleteStateIsDerivedAcrossSlots()
    {
        await using SnapshotSurveyWorkspace workspace =
            await CreateValidationWorkspaceAsync(
                ("file1.rpgsave", GoldJson("21", "22")),
                ("file2.rpgsave", "{\"variables\":{}}"));

        AtlasGoldSnapshotValidationSummary result =
            await workspace.RunGoldValidationAsync();

        AssertSummary(
            result,
            AtlasGoldSnapshotValidationState.DisagreementAndIncompleteObserved,
            total: 2,
            consistent: 0,
            disagree: 1,
            incomplete: 1);
    }

    [Fact]
    public void SummaryFactoryClosesReconciliationAndDerivedStateInvariants()
    {
        AtlasGoldSnapshotValidationSummary consistent =
            AtlasGoldSnapshotValidationSummary.Create(2, 2, 0, 0);
        AtlasGoldSnapshotValidationSummary disagree =
            AtlasGoldSnapshotValidationSummary.Create(2, 1, 1, 0);
        AtlasGoldSnapshotValidationSummary incomplete =
            AtlasGoldSnapshotValidationSummary.Create(2, 1, 0, 1);
        AtlasGoldSnapshotValidationSummary both =
            AtlasGoldSnapshotValidationSummary.Create(2, 0, 1, 1);

        Assert.Equal(AtlasGoldSnapshotValidationState.AllConsistent, consistent.State);
        Assert.Equal(
            AtlasGoldSnapshotValidationState.DisagreementObserved,
            disagree.State);
        Assert.Equal(
            AtlasGoldSnapshotValidationState.IncompleteObserved,
            incomplete.State);
        Assert.Equal(
            AtlasGoldSnapshotValidationState.DisagreementAndIncompleteObserved,
            both.State);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => AtlasGoldSnapshotValidationSummary.Create(0, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AtlasGoldSnapshotValidationSummary.Create(2, 1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AtlasGoldSnapshotValidationSummary.Create(1, -1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AtlasGoldSnapshotValidationSummary.Create(21, 21, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => AtlasGoldSnapshotValidationSummary.Create(
                1,
                int.MaxValue,
                int.MaxValue,
                3));
    }

    [Fact]
    public void PublicShapeExposesOnlyTheClosedSummaryAndRunnerContract()
    {
        Assert.Equal(
            [
                nameof(AtlasGoldSnapshotValidationState.AllConsistent),
                nameof(AtlasGoldSnapshotValidationState.DisagreementObserved),
                nameof(AtlasGoldSnapshotValidationState.IncompleteObserved),
                nameof(
                    AtlasGoldSnapshotValidationState
                        .DisagreementAndIncompleteObserved),
            ],
            Enum.GetNames<AtlasGoldSnapshotValidationState>());

        Type summaryType = typeof(AtlasGoldSnapshotValidationSummary);
        Assert.True(summaryType.IsSealed);
        Assert.Empty(summaryType.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(
            summaryType.GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.Equal(
            new Dictionary<string, Type>(StringComparer.Ordinal)
            {
                [nameof(AtlasGoldSnapshotValidationSummary.TotalSlots)] = typeof(int),
                [nameof(AtlasGoldSnapshotValidationSummary.Consistent)] = typeof(int),
                [nameof(AtlasGoldSnapshotValidationSummary.Disagree)] = typeof(int),
                [nameof(AtlasGoldSnapshotValidationSummary.Incomplete)] = typeof(int),
                [nameof(AtlasGoldSnapshotValidationSummary.State)] =
                    typeof(AtlasGoldSnapshotValidationState),
            },
            summaryType
                .GetProperties(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .ToDictionary(
                    static property => property.Name,
                    static property => property.PropertyType,
                    StringComparer.Ordinal));

        MethodInfo run = Assert.Single(
            typeof(AtlasGoldSnapshotValidation).GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.Equal(nameof(AtlasGoldSnapshotValidation.RunAsync), run.Name);
        Assert.Equal(
            typeof(ValueTask<AtlasGoldSnapshotValidationSummary>),
            run.ReturnType);
        ParameterInfo[] parameters = run.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal("requestFilePath", parameters[0].Name);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
        Assert.Equal("cancellationToken", parameters[1].Name);
        Assert.True(parameters[1].HasDefaultValue);
        Assert.Null(parameters[1].DefaultValue);
        Assert.False(typeof(AtlasGoldSnapshotValidationSeams).IsPublic);
    }

    [Fact]
    public async Task EverySlotIsReopenedExactlyOnceInReceiptOrderAndNonSlotsAreExcluded()
    {
        List<(string Name, byte[] Bytes)> files =
        [
            ("global.rpgsave", "invalid-global"u8.ToArray()),
            ("config.rpgsave", "invalid-config"u8.ToArray()),
        ];
        files.AddRange(
            Enumerable.Range(1, 20)
                .Select(index => (
                    $"file{index}.rpgsave",
                    AtlasLzStringCodec.CompressToBase64(
                        GoldJson(index.ToString(), index.ToString()),
                        cancellationToken: TestContext.Current.CancellationToken))));
        await using SnapshotSurveyWorkspace workspace =
            await CreateRawValidationWorkspaceAsync([.. files]);
        Dictionary<string, int> opens = new(StringComparer.Ordinal);
        List<string> postValidationOrder = [];
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            openFile: (path, mode, access, share, options) =>
            {
                string leaf = Path.GetFileName(path);
                if (mode == FileMode.Open
                    && access == FileAccess.Read
                    && leaf.EndsWith(".rpgsave", StringComparison.Ordinal))
                {
                    int count = opens.GetValueOrDefault(leaf) + 1;
                    opens[leaf] = count;
                    if (count == 2)
                    {
                        postValidationOrder.Add(leaf);
                    }
                }

                return AtlasIoSeams.Default.OpenFile(path, mode, access, share, options);
            });

        AtlasGoldSnapshotValidationSummary result =
            await workspace.RunGoldValidationAsync(io);

        AssertSummary(
            result,
            AtlasGoldSnapshotValidationState.AllConsistent,
            total: 20,
            consistent: 20,
            disagree: 0,
            incomplete: 0);
        Assert.Equal(
            Enumerable.Range(1, 20).Select(static index => $"file{index}.rpgsave"),
            postValidationOrder);
        Assert.Equal(1, opens["global.rpgsave"]);
        Assert.Equal(1, opens["config.rpgsave"]);
        Assert.All(
            Enumerable.Range(1, 20),
            index => Assert.Equal(2, opens[$"file{index}.rpgsave"]));
    }

    [Fact]
    public async Task ExcludedDocumentsDoNotUseTheSlotEncodedInputLimit()
    {
        byte[] slotBytes = AtlasLzStringCodec.CompressToBase64(
            GoldJson("19", "19"),
            cancellationToken: TestContext.Current.CancellationToken);
        await using SnapshotSurveyWorkspace workspace =
            await CreateRawValidationWorkspaceAsync(
                ("global.rpgsave", new byte[slotBytes.Length + 1]),
                ("config.rpgsave", new byte[slotBytes.Length + 2]),
                ("file1.rpgsave", slotBytes));
        AtlasSaveReaderLimits limits = AtlasSaveReaderLimits.Default with
        {
            MaximumEncodedBytes = slotBytes.Length,
        };

        AtlasGoldSnapshotValidationSummary result =
            await AtlasGoldSnapshotValidation.RunAsync(
                workspace.RequestPath,
                AtlasIoSeams.Default,
                limits,
                TestContext.Current.CancellationToken);

        AssertSummary(
            result,
            AtlasGoldSnapshotValidationState.AllConsistent,
            total: 1,
            consistent: 1,
            disagree: 0,
            incomplete: 0);
    }

    [Fact]
    public async Task SnapshotWithoutSlotsIsRefused()
    {
        await using SnapshotSurveyWorkspace workspace =
            await CreateRawValidationWorkspaceAsync(
                ("global.rpgsave", "not-a-save"u8.ToArray()),
                ("config.rpgsave", "not-a-save"u8.ToArray()));

        AtlasSafetyException exception = await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunGoldValidationAsync().AsTask());

        Assert.Equal("The finalized snapshot contains no slot saves.", exception.Message);
    }

    [Theory]
    [InlineData("changed")]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("receipt")]
    [InlineData("incomplete")]
    public async Task InvalidOrNonFinalizedSnapshotsAreRefused(string mutation)
    {
        await using SnapshotSurveyWorkspace workspace =
            await CreateValidationWorkspaceAsync(
                ("file1.rpgsave", GoldJson("31", "31")));
        string slotPath = Path.Combine(workspace.SnapshotRoot, "file1.rpgsave");
        switch (mutation)
        {
            case "changed":
                await File.AppendAllTextAsync(
                    slotPath,
                    "changed",
                    TestContext.Current.CancellationToken);
                break;
            case "missing":
                File.Delete(slotPath);
                break;
            case "extra":
                await File.WriteAllTextAsync(
                    Path.Combine(workspace.SnapshotRoot, "extra.bin"),
                    "extra",
                    TestContext.Current.CancellationToken);
                break;
            case "receipt":
                await File.WriteAllTextAsync(
                    workspace.SnapshotReceiptPath,
                    "{}",
                    TestContext.Current.CancellationToken);
                break;
            case "incomplete":
                Directory.Move(
                    workspace.SnapshotRoot,
                    workspace.SnapshotRoot + ".incomplete");
                break;
            default:
                throw new InvalidOperationException();
        }

        await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunGoldValidationAsync().AsTask());
    }

    [Theory]
    [InlineData("same-length-different-hash")]
    [InlineData("different-length")]
    [InlineData("post-read-length")]
    public async Task ReadTimeSubstitutionIsRefusedBeforeParsing(string mutation)
    {
        await using SnapshotSurveyWorkspace workspace =
            await CreateValidationWorkspaceAsync(
                ("file1.rpgsave", GoldJson("41", "41")));
        string slotPath = Path.Combine(workspace.SnapshotRoot, "file1.rpgsave");
        byte[] original = await File.ReadAllBytesAsync(
            slotPath,
            TestContext.Current.CancellationToken);
        byte[] substitute = mutation == "different-length"
            ? original[..^1]
            : [.. original];
        if (mutation == "same-length-different-hash")
        {
            substitute[0] = substitute[0] == (byte)'A' ? (byte)'B' : (byte)'A';
        }

        int opens = 0;
        int lengthReads = 0;
        AtlasGoldSnapshotValidationSeams seams = new()
        {
            ReadSave = (_, _, _) =>
                throw new InvalidOperationException("Parsing must not begin."),
        };
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            openFile: (path, mode, access, share, options) =>
            {
                if (AtlasSaveSnapshotContracts.PathEquals(path, slotPath)
                    && mode == FileMode.Open
                    && access == FileAccess.Read
                    && ++opens == 2
                    && mutation != "post-read-length")
                {
                    return new MemoryStream(substitute, writable: false);
                }

                return AtlasIoSeams.Default.OpenFile(path, mode, access, share, options);
            },
            getLength: path =>
            {
                long length = AtlasIoSeams.Default.GetLength(path);
                if (AtlasSaveSnapshotContracts.PathEquals(path, slotPath)
                    && ++lengthReads == 2
                    && mutation == "post-read-length")
                {
                    return length + 1;
                }

                return length;
            });

        AtlasSafetyException exception = await Assert.ThrowsAsync<AtlasSafetyException>(
            () => workspace.RunGoldValidationAsync(
                io,
                seams,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(
            "A validated snapshot copy changed during reading.",
            exception.Message);
        Assert.DoesNotContain(slotPath, exception.Message, StringComparison.Ordinal);
        Assert.Equal(2, opens);
    }

    [Fact]
    public async Task PostValidationIoFailureRetainsItsTypedBehavior()
    {
        await using SnapshotSurveyWorkspace workspace =
            await CreateValidationWorkspaceAsync(
                ("file1.rpgsave", GoldJson("51", "51")));
        string slotPath = Path.Combine(workspace.SnapshotRoot, "file1.rpgsave");
        int opens = 0;
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            openFile: (path, mode, access, share, options) =>
            {
                if (AtlasSaveSnapshotContracts.PathEquals(path, slotPath)
                    && mode == FileMode.Open
                    && access == FileAccess.Read
                    && ++opens == 2)
                {
                    throw new IOException("synthetic detail");
                }

                return AtlasIoSeams.Default.OpenFile(path, mode, access, share, options);
            });

        await Assert.ThrowsAsync<IOException>(
            () => workspace.RunGoldValidationAsync(io).AsTask());
    }

    [Fact]
    public async Task ParseFailureRefusesTheWholeOperationWithoutAResult()
    {
        byte[] valid = AtlasLzStringCodec.CompressToBase64(
            GoldJson("61", "61"),
            cancellationToken: TestContext.Current.CancellationToken);
        await using SnapshotSurveyWorkspace workspace =
            await CreateRawValidationWorkspaceAsync(
                ("file1.rpgsave", valid),
                ("file2.rpgsave", "invalid-base64"u8.ToArray()));
        int classifications = 0;
        AtlasGoldSnapshotValidationSeams seams = new()
        {
            ReadGold = (source, cancellationToken) =>
            {
                classifications++;
                return AtlasGoldReadModel.Read(source, cancellationToken);
            },
        };

        await Assert.ThrowsAsync<AtlasSaveReadException>(
            () => workspace.RunGoldValidationAsync(
                AtlasIoSeams.Default,
                seams,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(1, classifications);
    }

    [Fact]
    public async Task RunnerIsReadOnlyAvoidsLiveSourcesAndPreservesA3Observations()
    {
        string json = GoldJson("71", "71");
        await using SnapshotSurveyWorkspace workspace =
            await CreateValidationWorkspaceAsync(("file1.rpgsave", json));
        AtlasIoSeams.Default.DeleteDirectory(workspace.SaveRoot, true);
        IReadOnlyDictionary<string, WorkspaceInventoryEntry> before =
            await ReadWorkspaceInventoryAsync(workspace.Root, AtlasIoSeams.Default);
        AtlasIoSeams io = CreateReadOnlyLiveSourceRejectingIo(workspace.SaveRoot);
        AtlasSaveReadResult? classifiedSource = null;
        A3SourceObservations? beforeA6 = null;
        AtlasGoldSnapshotValidationSeams seams = new()
        {
            ReadGold = (source, cancellationToken) =>
            {
                Assert.Null(classifiedSource);
                classifiedSource = source;
                beforeA6 = CaptureA3SourceObservations(source, cancellationToken);
                return AtlasGoldReadModel.Read(source, cancellationToken);
            },
        };

        AtlasGoldSnapshotValidationSummary result =
            await workspace.RunGoldValidationAsync(
                io,
                seams,
                TestContext.Current.CancellationToken);

        Assert.Equal(AtlasGoldSnapshotValidationState.AllConsistent, result.State);
        AssertWorkspaceInventoryEqual(
            before,
            await ReadWorkspaceInventoryAsync(workspace.Root, AtlasIoSeams.Default));
        AssertA3SourceObservationsUnchanged(
            classifiedSource
                ?? throw new InvalidOperationException("A3 source was not classified."),
            beforeA6
                ?? throw new InvalidOperationException("A3 observations were not captured."));
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("wrong-type")]
    [InlineData("schema")]
    [InlineData("relative-repository")]
    [InlineData("relative-receipt")]
    [InlineData("malformed")]
    [InlineData("deep")]
    [InlineData("long-number")]
    public async Task RequestParsingIsStrict(string mutation)
    {
        await using SnapshotSurveyWorkspace workspace =
            await CreateValidationWorkspaceAsync(
                ("file1.rpgsave", GoldJson("81", "81")));
        string json = await File.ReadAllTextAsync(
            workspace.RequestPath,
            TestContext.Current.CancellationToken);
        JsonObject request = JsonNode.Parse(json)!.AsObject();
        json = mutation switch
        {
            "unknown" => json[..^1] + ",\"unknown\":true}",
            "duplicate" => json.Replace(
                "\"repositoryRoot\":",
                $"\"repositoryRoot\":\"{EscapeJson(workspace.RepositoryRoot)}\","
                    + "\"repositoryRoot\":",
                StringComparison.Ordinal),
            "missing" => Mutate(request, static value => value.Remove("repositoryRoot")),
            "null" => Mutate(request, static value => value["snapshotReceiptPath"] = null),
            "wrong-type" => Mutate(request, static value => value["repositoryRoot"] = 1),
            "schema" => Mutate(
                request,
                static value => value["schemaVersion"] = "unsupported"),
            "relative-repository" => Mutate(
                request,
                static value => value["repositoryRoot"] = "relative"),
            "relative-receipt" => Mutate(
                request,
                static value => value["snapshotReceiptPath"] = "relative"),
            "malformed" => "{",
            "deep" => json[..^1]
                + ",\"x\":{\"a\":{\"b\":{\"c\":{\"d\":{\"e\":{\"f\":{\"g\":1}}}}}}}}",
            "long-number" => json[..^1] + ",\"x\":123456789012345678901}",
            _ => throw new InvalidOperationException(),
        };
        await File.WriteAllTextAsync(
            workspace.RequestPath,
            json,
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AtlasRequestException>(
            () => workspace.RunGoldValidationAsync().AsTask());
    }

    [Fact]
    public async Task RequestByteStringAndTokenLimitsAreEnforced()
    {
        await using SnapshotSurveyWorkspace workspace =
            await CreateValidationWorkspaceAsync(
                ("file1.rpgsave", GoldJson("91", "91")));
        await File.WriteAllTextAsync(
            workspace.RequestPath,
            new string(' ', AtlasGoldSnapshotValidationContracts.MaximumRequestBytes + 1),
            TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<AtlasRequestException>(
            () => workspace.RunGoldValidationAsync().AsTask());

        JsonObject longString = CreateRequest(workspace);
        longString["repositoryRoot"] = new string(
            'a',
            AtlasGoldSnapshotValidationContracts.MaximumStringLength + 1);
        await WriteObjectAsync(workspace.RequestPath, longString);
        await Assert.ThrowsAsync<AtlasRequestException>(
            () => workspace.RunGoldValidationAsync().AsTask());

        JsonObject manyTokens = CreateRequest(workspace);
        for (int index = 0;
             index < AtlasGoldSnapshotValidationContracts.MaximumRequestTokens;
             index++)
        {
            manyTokens[$"x{index}"] = index;
        }

        await WriteObjectAsync(workspace.RequestPath, manyTokens);
        await Assert.ThrowsAsync<AtlasRequestException>(
            () => workspace.RunGoldValidationAsync().AsTask());
    }

    [Fact]
    public async Task RequestSchemaMatchesRuntimeShapeAndRejectsMutations()
    {
        await using SnapshotSurveyWorkspace workspace =
            await CreateValidationWorkspaceAsync(
                ("file1.rpgsave", GoldJson("101", "101")));
        using JsonDocument schema = JsonDocument.Parse(
            await File.ReadAllBytesAsync(
                SnapshotSurveyWorkspace.GetSchemaPath(
                    "atlas-gold-snapshot-validation-request.schema.json"),
                TestContext.Current.CancellationToken));
        using JsonDocument valid = JsonDocument.Parse(
            await File.ReadAllBytesAsync(
                workspace.RequestPath,
                TestContext.Current.CancellationToken));
        Assert.True(IsRequestSchemaValid(
            schema.RootElement,
            valid.RootElement,
            schema.RootElement));

        JsonObject[] invalidRequests =
        [
            MutatedRequest(workspace, static value => value["unknown"] = true),
            MutatedRequest(workspace, static value => value.Remove("repositoryRoot")),
            MutatedRequest(
                workspace,
                static value => value["schemaVersion"] = "unsupported"),
            MutatedRequest(workspace, static value => value["repositoryRoot"] = 1),
            MutatedRequest(workspace, static value => value["snapshotReceiptPath"] = ""),
        ];
        foreach (JsonObject invalidRequest in invalidRequests)
        {
            using JsonDocument invalid = JsonDocument.Parse(invalidRequest.ToJsonString());
            Assert.False(IsRequestSchemaValid(
                schema.RootElement,
                invalid.RootElement,
                schema.RootElement));
        }

        string[] schemas = Directory
            .EnumerateFiles(
                Path.GetDirectoryName(
                    SnapshotSurveyWorkspace.GetSchemaPath(
                        "atlas-gold-snapshot-validation-request.schema.json"))!,
                "atlas-gold-snapshot-validation*.schema.json")
            .Select(Path.GetFileName)
            .ToArray()!;
        Assert.Equal(
            ["atlas-gold-snapshot-validation-request.schema.json"],
            schemas);
    }

    [Fact]
    public async Task CancellationIsObservedAcrossValidationReadingParsingAndClassification()
    {
        await using SnapshotSurveyWorkspace workspace =
            await CreateValidationWorkspaceAsync(
                ("file1.rpgsave", GoldJson("111", "111")),
                ("file2.rpgsave", GoldJson("112", "112")));
        string firstSlot = Path.Combine(workspace.SnapshotRoot, "file1.rpgsave");

        using CancellationTokenSource before = new();
        await before.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => workspace.RunGoldValidationAsync(
                AtlasIoSeams.Default,
                AtlasGoldSnapshotValidationSeams.Default,
                before.Token).AsTask());

        await AssertCanceledOnSlotOpenAsync(
            workspace,
            firstSlot,
            targetOpen: 1);
        await AssertCanceledOnSlotOpenAsync(
            workspace,
            firstSlot,
            targetOpen: 2);

        using CancellationTokenSource parsing = new();
        bool parsingEntered = false;
        AtlasGoldSnapshotValidationSeams parsingSeams = new()
        {
            ReadSave = (bytes, limits, token) =>
            {
                parsingEntered = true;
                Assert.Equal(parsing.Token, token);
                AtlasSaveReadResult result = AtlasSaveReader.Read(bytes, limits, token);
                parsing.Cancel();
                return result;
            },
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => workspace.RunGoldValidationAsync(
                AtlasIoSeams.Default,
                parsingSeams,
                parsing.Token).AsTask());
        Assert.True(parsingEntered);

        using CancellationTokenSource classification = new();
        bool classificationEntered = false;
        AtlasGoldSnapshotValidationSeams classificationSeams = new()
        {
            ReadGold = (source, token) =>
            {
                classificationEntered = true;
                Assert.Equal(classification.Token, token);
                AtlasGoldReadModelResult result =
                    AtlasGoldReadModel.Read(source, token);
                classification.Cancel();
                return result;
            },
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => workspace.RunGoldValidationAsync(
                AtlasIoSeams.Default,
                classificationSeams,
                classification.Token).AsTask());
        Assert.True(classificationEntered);
    }

    [Fact]
    public async Task CancellationChecksPrecedeAggregateUpdatesAndReturn()
    {
        await using SnapshotSurveyWorkspace workspace =
            await CreateValidationWorkspaceAsync(
                ("file1.rpgsave", GoldJson("121", "121")),
                ("file2.rpgsave", GoldJson("122", "122")));
        using CancellationTokenSource aggregate = new();
        int classifications = 0;
        AtlasGoldSnapshotValidationSeams aggregateSeams = new()
        {
            ReadGold = (source, cancellationToken) =>
            {
                AtlasGoldReadModelResult result =
                    AtlasGoldReadModel.Read(source, cancellationToken);
                classifications++;
                aggregate.Cancel();
                return result;
            },
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => workspace.RunGoldValidationAsync(
                AtlasIoSeams.Default,
                aggregateSeams,
                aggregate.Token).AsTask());
        Assert.Equal(1, classifications);

        using CancellationTokenSource beforeReturn = new();
        int summaries = 0;
        AtlasGoldSnapshotValidationSeams returnSeams = new()
        {
            CreateSummary = (total, consistent, disagree, incomplete) =>
            {
                AtlasGoldSnapshotValidationSummary result =
                    AtlasGoldSnapshotValidationSummary.Create(
                        total,
                        consistent,
                        disagree,
                        incomplete);
                summaries++;
                beforeReturn.Cancel();
                return result;
            },
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => workspace.RunGoldValidationAsync(
                AtlasIoSeams.Default,
                returnSeams,
                beforeReturn.Token).AsTask());
        Assert.Equal(1, summaries);
    }

    private static async Task AssertCanceledOnSlotOpenAsync(
        SnapshotSurveyWorkspace workspace,
        string slotPath,
        int targetOpen)
    {
        using CancellationTokenSource cancellation = new();
        int opens = 0;
        AtlasIoSeams io = AtlasTestSupport.CreateIo(
            openFile: (path, mode, access, share, options) =>
            {
                Stream stream = AtlasIoSeams.Default.OpenFile(
                    path,
                    mode,
                    access,
                    share,
                    options);
                return AtlasSaveSnapshotContracts.PathEquals(path, slotPath)
                    && mode == FileMode.Open
                    && access == FileAccess.Read
                    && ++opens == targetOpen
                    ? new CancelingReadStream(stream, cancellation)
                    : stream;
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => workspace.RunGoldValidationAsync(
                io,
                AtlasGoldSnapshotValidationSeams.Default,
                cancellation.Token).AsTask());
    }

    private static AtlasIoSeams CreateReadOnlyLiveSourceRejectingIo(string saveRoot) =>
        AtlasTestSupport.CreateIo(
            readAllBytesAsync: (path, cancellationToken) =>
                AccessNonLive(
                    saveRoot,
                    path,
                    () => AtlasIoSeams.Default.ReadAllBytesAsync(
                        path,
                        cancellationToken)),
            fileExists: path =>
                AccessNonLive(
                    saveRoot,
                    path,
                    () => AtlasIoSeams.Default.FileExists(path)),
            directoryExists: path =>
                AccessNonLive(
                    saveRoot,
                    path,
                    () => AtlasIoSeams.Default.DirectoryExists(path)),
            getAttributes: path =>
                AccessNonLive(
                    saveRoot,
                    path,
                    () => AtlasIoSeams.Default.GetAttributes(path)),
            tryGetDirectoryFinalPath: path =>
                AccessNonLive(
                    saveRoot,
                    path,
                    () => AtlasIoSeams.Default.TryGetDirectoryFinalPath(path)),
            getDriveInfo: path =>
                AccessNonLive(
                    saveRoot,
                    path,
                    () => AtlasIoSeams.Default.GetDriveInfo(path)),
            enumerateFileSystemEntries: (path, option) =>
                AccessNonLive(
                    saveRoot,
                    path,
                    () => AtlasIoSeams.Default.EnumerateFileSystemEntries(path, option)),
            openFile: (path, mode, access, share, options) =>
            {
                if (mode != FileMode.Open || access != FileAccess.Read)
                {
                    throw new InvalidOperationException("Output writes are forbidden.");
                }

                return AccessNonLive(
                    saveRoot,
                    path,
                    () => AtlasIoSeams.Default.OpenFile(
                        path,
                        mode,
                        access,
                        share,
                        options));
            },
            createDirectory: _ => ThrowOutputWrite(),
            moveFile: (_, _) => ThrowOutputWrite(),
            moveDirectory: (_, _) => ThrowOutputWrite(),
            replaceFile: (_, _, _) => ThrowOutputWrite(),
            deleteDirectory: (_, _) => ThrowOutputWrite(),
            deleteFile: _ => ThrowOutputWrite(),
            setAttributes: (_, _) => ThrowOutputWrite(),
            getLength: path =>
                AccessNonLive(
                    saveRoot,
                    path,
                    () => AtlasIoSeams.Default.GetLength(path)),
            getLastWriteTimeUtc: path =>
                AccessNonLive(
                    saveRoot,
                    path,
                    () => AtlasIoSeams.Default.GetLastWriteTimeUtc(path)));

    private static T AccessNonLive<T>(
        string saveRoot,
        string path,
        Func<T> operation)
    {
        if (IsLivePath(saveRoot, path))
        {
            throw new InvalidOperationException("Live source access is forbidden.");
        }

        return operation();
    }

    private static void ThrowOutputWrite() =>
        throw new InvalidOperationException("Output writes are forbidden.");

    private static bool IsLivePath(string saveRoot, string path) =>
        AtlasSaveSnapshotContracts.ContainsPath(saveRoot, path);

    private static async ValueTask<SnapshotSurveyWorkspace> CreateValidationWorkspaceAsync(
        params (string Name, string Json)[] files)
    {
        SnapshotSurveyWorkspace workspace =
            await SnapshotSurveyWorkspace.CreateAsync(files);
        await WriteValidationRequestAsync(workspace);
        return workspace;
    }

    private static async ValueTask<SnapshotSurveyWorkspace> CreateRawValidationWorkspaceAsync(
        params (string Name, byte[] Bytes)[] files)
    {
        SnapshotSurveyWorkspace workspace =
            await SnapshotSurveyWorkspace.CreateRawAsync(files);
        await WriteValidationRequestAsync(workspace);
        return workspace;
    }

    private static Task WriteValidationRequestAsync(SnapshotSurveyWorkspace workspace) =>
        WriteObjectAsync(workspace.RequestPath, CreateRequest(workspace));

    private static JsonObject CreateRequest(SnapshotSurveyWorkspace workspace) =>
        new()
        {
            ["schemaVersion"] =
                AtlasGoldSnapshotValidationContracts.RequestSchemaVersion,
            ["repositoryRoot"] = workspace.RepositoryRoot,
            ["snapshotReceiptPath"] = workspace.SnapshotReceiptPath,
        };

    private static JsonObject MutatedRequest(
        SnapshotSurveyWorkspace workspace,
        Action<JsonObject> mutation)
    {
        JsonObject request = CreateRequest(workspace);
        mutation(request);
        return request;
    }

    private static Task WriteObjectAsync(string path, JsonObject value) =>
        File.WriteAllTextAsync(
            path,
            value.ToJsonString(),
            new UTF8Encoding(false),
            TestContext.Current.CancellationToken);

    private static string Mutate(JsonObject value, Action<JsonObject> mutation)
    {
        mutation(value);
        return value.ToJsonString();
    }

    private static string EscapeJson(string value) =>
        JsonSerializer.Serialize(value)[1..^1];

    private static string GoldJson(string partyGold, string variableGold) =>
        $"{{\"party\":{{\"_gold\":{partyGold}}},"
        + $"\"variables\":{{\"_data\":{DataArray(variableGold)}}}}}";

    private static string DataArray(string gold)
    {
        string[] values = Enumerable.Repeat("0", 216).ToArray();
        values[215] = gold;
        return $"[{string.Join(",", values)}]";
    }

    private static async Task<IReadOnlyDictionary<string, WorkspaceInventoryEntry>>
        ReadWorkspaceInventoryAsync(
            string root,
            AtlasIoSeams io)
    {
        List<string> paths = [root];
        paths.AddRange(io.EnumerateFileSystemEntries(root, SearchOption.AllDirectories));
        Dictionary<string, WorkspaceInventoryEntry> result =
            new(StringComparer.Ordinal);
        foreach (string path in paths.Order(StringComparer.Ordinal))
        {
            FileAttributes attributes = io.GetAttributes(path);
            bool isDirectory = (attributes & FileAttributes.Directory) != 0;
            byte[]? bytes = isDirectory
                ? null
                : await io.ReadAllBytesAsync(
                    path,
                    TestContext.Current.CancellationToken);
            result.Add(
                Path.GetRelativePath(root, path),
                new WorkspaceInventoryEntry(
                    attributes,
                    isDirectory ? null : io.GetLength(path),
                    isDirectory ? null : io.GetLastWriteTimeUtc(path),
                    bytes));
        }

        return result;
    }

    private static void AssertWorkspaceInventoryEqual(
        IReadOnlyDictionary<string, WorkspaceInventoryEntry> expected,
        IReadOnlyDictionary<string, WorkspaceInventoryEntry> actual)
    {
        Assert.Equal(
            expected.Keys.Order(StringComparer.Ordinal),
            actual.Keys.Order(StringComparer.Ordinal));
        foreach ((string path, WorkspaceInventoryEntry expectedEntry) in expected)
        {
            WorkspaceInventoryEntry actualEntry = actual[path];
            Assert.Equal(expectedEntry.Attributes, actualEntry.Attributes);
            Assert.Equal(expectedEntry.Length, actualEntry.Length);
            Assert.Equal(expectedEntry.LastWriteTimeUtc, actualEntry.LastWriteTimeUtc);
            Assert.Equal(expectedEntry.Bytes, actualEntry.Bytes);
        }
    }

    private static A3SourceObservations CaptureA3SourceObservations(
        AtlasSaveReadResult source,
        CancellationToken cancellationToken) =>
        new(
            source.OriginalCompressedBytes.ToArray(),
            source.Json.Utf8Source.ToArray(),
            source.GetSemanticNoOpBytes(),
            source.TokenCensus,
            source.GraphCensus,
            AtlasStructuralScanner
                .Scan(
                    source,
                    AtlasDocumentRole.SlotSave,
                    cancellationToken: cancellationToken)
                .GetCanonicalUtf8Bytes(cancellationToken));

    private static void AssertA3SourceObservationsUnchanged(
        AtlasSaveReadResult source,
        A3SourceObservations expected)
    {
        Assert.Equal(expected.OriginalCompressedBytes, source.OriginalCompressedBytes.ToArray());
        Assert.Equal(expected.LosslessJsonSource, source.Json.Utf8Source.ToArray());
        Assert.Equal(expected.SemanticNoOpBytes, source.GetSemanticNoOpBytes());
        Assert.Equal(expected.TokenCensus, source.TokenCensus);
        Assert.Equal(expected.GraphCensus, source.GraphCensus);
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        Assert.Equal(
            expected.StructuralObservations,
            AtlasStructuralScanner
                .Scan(
                    source,
                    AtlasDocumentRole.SlotSave,
                    cancellationToken: cancellationToken)
                .GetCanonicalUtf8Bytes(cancellationToken));
    }

    private static void AssertSummary(
        AtlasGoldSnapshotValidationSummary summary,
        AtlasGoldSnapshotValidationState state,
        int total,
        int consistent,
        int disagree,
        int incomplete)
    {
        Assert.Equal(state, summary.State);
        Assert.Equal(total, summary.TotalSlots);
        Assert.Equal(consistent, summary.Consistent);
        Assert.Equal(disagree, summary.Disagree);
        Assert.Equal(incomplete, summary.Incomplete);
        Assert.Equal(
            summary.TotalSlots,
            summary.Consistent + summary.Disagree + summary.Incomplete);
    }

    private static bool IsRequestSchemaValid(
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

        if (schema.TryGetProperty("type", out JsonElement type))
        {
            bool typeMatches = type.GetString() switch
            {
                "object" => instance.ValueKind == JsonValueKind.Object,
                "string" => instance.ValueKind == JsonValueKind.String,
                _ => true,
            };
            if (!typeMatches)
            {
                return false;
            }
        }

        if (instance.ValueKind == JsonValueKind.String)
        {
            int length = instance.GetString()!.Length;
            if (schema.TryGetProperty("minLength", out JsonElement minimum)
                && length < minimum.GetInt32()
                || schema.TryGetProperty("maxLength", out JsonElement maximum)
                    && length > maximum.GetInt32())
            {
                return false;
            }
        }

        if (instance.ValueKind != JsonValueKind.Object)
        {
            return true;
        }

        JsonElement properties = schema.GetProperty("properties");
        if (schema.GetProperty("required")
            .EnumerateArray()
            .Any(name => !instance.TryGetProperty(name.GetString()!, out _)))
        {
            return false;
        }

        if (schema.GetProperty("additionalProperties").ValueKind == JsonValueKind.False
            && instance.EnumerateObject().Any(
                property => !properties.TryGetProperty(property.Name, out _)))
        {
            return false;
        }

        foreach (JsonProperty property in instance.EnumerateObject())
        {
            if (!properties.TryGetProperty(property.Name, out JsonElement propertySchema)
                || !IsRequestSchemaValid(propertySchema, property.Value, rootSchema))
            {
                return false;
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
            schema = rootSchema;
            foreach (string segment in reference.GetString()![2..].Split('/'))
            {
                schema = schema.GetProperty(segment);
            }
        }

        return schema;
    }

    private sealed record WorkspaceInventoryEntry(
        FileAttributes Attributes,
        long? Length,
        DateTimeOffset? LastWriteTimeUtc,
        byte[]? Bytes);

    private sealed record A3SourceObservations(
        byte[] OriginalCompressedBytes,
        byte[] LosslessJsonSource,
        byte[] SemanticNoOpBytes,
        AtlasTokenCensus TokenCensus,
        AtlasGraphCensus GraphCensus,
        byte[] StructuralObservations);

    private sealed class CancelingReadStream(
        Stream inner,
        CancellationTokenSource cancellation) : Stream
    {
        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            return ValueTask.FromException<int>(
                new OperationCanceledException(cancellationToken));
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            inner.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

internal static class AtlasGoldSnapshotValidationTestExtensions
{
    public static ValueTask<AtlasGoldSnapshotValidationSummary> RunGoldValidationAsync(
        this SnapshotSurveyWorkspace workspace) =>
        AtlasGoldSnapshotValidation.RunAsync(
            workspace.RequestPath,
            TestContext.Current.CancellationToken);

    public static ValueTask<AtlasGoldSnapshotValidationSummary> RunGoldValidationAsync(
        this SnapshotSurveyWorkspace workspace,
        AtlasIoSeams io) =>
        AtlasGoldSnapshotValidation.RunAsync(
            workspace.RequestPath,
            io,
            AtlasSaveReaderLimits.Default,
            TestContext.Current.CancellationToken);

    public static ValueTask<AtlasGoldSnapshotValidationSummary> RunGoldValidationAsync(
        this SnapshotSurveyWorkspace workspace,
        AtlasIoSeams io,
        AtlasGoldSnapshotValidationSeams seams,
        CancellationToken cancellationToken) =>
        AtlasGoldSnapshotValidation.RunAsync(
            workspace.RequestPath,
            io,
            AtlasSaveReaderLimits.Default,
            seams,
            cancellationToken);
}
