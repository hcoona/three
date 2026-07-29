using System.Text;
using Hcoona.CelesphoniaModifier.Atlas;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class AtlasSaveReaderTests
{
    public static TheoryData<string, string> PublicCodecVectors =>
        new()
        {
            { string.Empty, "Q===" },
            { "hello", "BYUwNmD2Q===" },
            { "Hello world", "BIUwNmD2AEDukCcwBMg=" },
            { "雪と星😀", "ldpgsGT4ZovBuAB7Q===" },
            { "\ud800", "gAbQ" },
            { "\udc00", "gA7Q" },
        };

    public static TheoryData<byte[], AtlasLzStringFailure> InvalidCodecGrammar =>
        new()
        {
            {
                Encoding.ASCII.GetBytes("Q=="),
                AtlasLzStringFailure.InvalidPadding
            },
            {
                Encoding.ASCII.GetBytes("Q===="),
                AtlasLzStringFailure.InvalidPadding
            },
            {
                Encoding.ASCII.GetBytes("Q=AA"),
                AtlasLzStringFailure.InvalidPadding
            },
            {
                Encoding.ASCII.GetBytes("Q == "),
                AtlasLzStringFailure.InvalidPadding
            },
            {
                [0xEF, 0xBB, 0xBF, (byte)'Q'],
                AtlasLzStringFailure.InvalidAlphabet
            },
        };

    [Theory]
    [MemberData(nameof(PublicCodecVectors))]
    public void CodecMatchesReviewedPublicVectors(string value, string encoded)
    {
        byte[] expected = Encoding.ASCII.GetBytes(encoded);

        Assert.Equal(
            expected,
            AtlasLzStringCodec.CompressToBase64(
                value,
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(
            value,
            AtlasLzStringCodec.DecompressFromBase64(
                expected,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void CodecRoundTripsUnicodeSurrogatesAndLargeBoundedText()
    {
        string[] values =
        [
            "ASCII",
            "雪と星😀",
            "\ud800",
            "\udc00",
            string.Concat(Enumerable.Repeat("synthetic-😀-", 4096)),
        ];

        foreach (string value in values)
        {
            byte[] encoded = AtlasLzStringCodec.CompressToBase64(
                value,
                cancellationToken: TestContext.Current.CancellationToken);
            Assert.Equal(
                value,
                AtlasLzStringCodec.DecompressFromBase64(
                    encoded,
                    cancellationToken: TestContext.Current.CancellationToken));
        }
    }

    [Fact]
    public void CodecRepetitiveInputHasBoundedAllocationGrowth()
    {
        _ = AtlasLzStringCodec.CompressToBase64(
            "warmup",
            cancellationToken: TestContext.Current.CancellationToken);
        string value = new('a', 512 * 1024);

        long before = GC.GetAllocatedBytesForCurrentThread();
        byte[] encoded = AtlasLzStringCodec.CompressToBase64(
            value,
            cancellationToken: TestContext.Current.CancellationToken);
        string decoded = AtlasLzStringCodec.DecompressFromBase64(
            encoded,
            cancellationToken: TestContext.Current.CancellationToken);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(value, decoded);
        Assert.True(
            allocated < value.Length * 64L,
            $"Repetitive codec work allocated {allocated} bytes.");
    }

    [Theory]
    [MemberData(nameof(InvalidCodecGrammar))]
    public void CodecRejectsInvalidAlphabetAndPadding(
        byte[] encoded,
        AtlasLzStringFailure expected)
    {
        AtlasLzStringException exception = Assert.Throws<AtlasLzStringException>(
            () => AtlasLzStringCodec.DecompressFromBase64(
                encoded,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(expected, exception.Failure);
    }

    [Theory]
    [InlineData("AAAA")]
    [InlineData("QAAA")]
    [InlineData("BYUwNmD2QAAA")]
    public void CodecRejectsMalformedTruncatedOrNoncanonicalStreams(string encoded)
    {
        AtlasLzStringException exception = Assert.Throws<AtlasLzStringException>(
            () => AtlasLzStringCodec.DecompressFromBase64(
                Encoding.ASCII.GetBytes(encoded),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(AtlasLzStringFailure.MalformedOrTruncated, exception.Failure);
    }

    [Fact]
    public async Task CodecStreamLimitsAndCancellationAreObserved()
    {
        AtlasSaveReaderLimits lowEncoded = new()
        {
            MaximumEncodedBytes = 3,
        };
        Assert.Throws<AtlasLzStringException>(
            () => AtlasLzStringCodec.CompressToBase64(
                "hello",
                lowEncoded,
                TestContext.Current.CancellationToken));
        await using MemoryStream stream = new("BYUwNmD2Q==="u8.ToArray());
        await Assert.ThrowsAsync<AtlasLzStringException>(
            () => AtlasLzStringCodec.DecompressFromBase64Async(
                stream,
                lowEncoded,
                TestContext.Current.CancellationToken).AsTask());

        using CancellationTokenSource source = new();
        await source.CancelAsync();
        Assert.ThrowsAny<OperationCanceledException>(
            () => AtlasLzStringCodec.CompressToBase64("hello", cancellationToken: source.Token));
    }

    [Fact]
    public async Task CodecCancellationPrecedesLargeGrammarPreprocessing()
    {
        byte[] encoded = new byte[4 * 1024 * 1024];
        Array.Fill(encoded, (byte)'A');
        using CancellationTokenSource source = new();
        await source.CancelAsync();

        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.ThrowsAny<OperationCanceledException>(
            () => AtlasLzStringCodec.DecompressFromBase64(
                encoded,
                cancellationToken: source.Token));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(
            allocated < 128 * 1024,
            $"Canceled preprocessing allocated {allocated} bytes.");
    }

    [Fact]
    public void LosslessJsonPreservesOrderDuplicatesUnknownsAndScalarLexemes()
    {
        const string json =
            "{\"b\":1,\"a\":-0,\"a\":1e+2,\"\\u0075\":\"\\u0041\","
            + "\"flag\":true,\"none\":null,\"@x\":false,\"@@\":0.50}";

        AtlasSaveReadResult result = ReadJson(json);
        AtlasLosslessJsonObject root = Assert.IsType<AtlasLosslessJsonObject>(
            result.Json.Root);

        Assert.Equal(
            ["b", "a", "a", "u", "flag", "none", "@x", "@@"],
            root.Members.Select(static member => member.Name));
        Assert.Equal(
            ["1", "-0", "1e+2", "\"\\u0041\"", "true", "null", "false", "0.50"],
            root.Members.Select(static member =>
                Assert.IsType<AtlasLosslessJsonScalar>(member.Value).RawLexeme));
        Assert.Equal("\"\\u0075\"", root.Members[3].RawNameLexeme);
        Assert.Equal(
            Encoding.UTF8.GetBytes(json),
            result.Json.Utf8Source.ToArray());
        AtlasJsonExObject graph = Assert.IsType<AtlasJsonExObject>(result.Graph);
        Assert.Equal(
            ["b", "a", "a", "u", "flag", "none", "@x", "@@"],
            graph.Members.Select(static member => member.Name));
    }

    [Fact]
    public void JsonExResolvesForwardBackwardSharedReferencesAndOpaqueClass()
    {
        const string json =
            "{\"forward\":{\"@r\":2},"
            + "\"target\":{\"@c\":2,\"@\":\"Synthetic.Type\",\"value\":1},"
            + "\"back\":{\"@r\":2}}";

        AtlasSaveReadResult result = ReadJson(json);
        AtlasJsonExObject root = Assert.IsType<AtlasJsonExObject>(result.Graph);
        AtlasJsonExReference forward = Assert.IsType<AtlasJsonExReference>(
            root.Members[0].Value);
        AtlasJsonExObject target = Assert.IsType<AtlasJsonExObject>(
            root.Members[1].Value);
        AtlasJsonExReference backward = Assert.IsType<AtlasJsonExReference>(
            root.Members[2].Value);

        Assert.Same(target, forward.Target);
        Assert.Same(target, backward.Target);
        Assert.Equal(2, target.Identity);
        Assert.Equal("Synthetic.Type", target.OpaqueClass);
        Assert.Equal(1, result.GraphCensus.IdentityDefinitions);
        Assert.Equal(2, result.GraphCensus.ReferenceEdges);
        Assert.Equal(5, result.GraphCensus.MaterializedNodes);
        Assert.Equal(1, result.GraphCensus.SharedTargets);
        Assert.True(
            AtlasCensusReconciliation.IsConsistent(
                result.TokenCensus,
                result.GraphCensus));
    }

    [Fact]
    public void CensusReconciliationRejectsOmittedOrdinaryGraphNodes()
    {
        AtlasTokenCensus tokenCensus = new(
            Containers: 1,
            MemberOccurrences: 100,
            ArrayElements: 0,
            Scalars: 100,
            IdentityMarkers: 0,
            ClassMarkers: 0,
            ArrayMarkers: 0,
            ReferenceMarkers: 0);
        AtlasGraphCensus graphCensus = new(
            MaterializedNodes: 1,
            IdentityDefinitions: 0,
            ReferenceEdges: 0,
            SharedTargets: 0,
            Cycles: 0);

        Assert.False(
            AtlasCensusReconciliation.IsConsistent(tokenCensus, graphCensus));
    }

    [Fact]
    public void JsonExPreservesObjectAndArraySelfCycles()
    {
        AtlasSaveReadResult objectResult = ReadJson(
            "{\"@c\":1,\"self\":{\"@r\":1},\"again\":{\"@r\":1}}");
        AtlasJsonExObject objectGraph = Assert.IsType<AtlasJsonExObject>(
            objectResult.Graph);
        Assert.All(
            objectGraph.Members,
            member => Assert.Same(
                objectGraph,
                Assert.IsType<AtlasJsonExReference>(member.Value).Target));
        Assert.True(objectResult.GraphCensus.Cycles > 0);

        AtlasSaveReadResult arrayResult = ReadJson(
            "{\"@c\":3,\"@a\":[{\"@r\":3}]}");
        AtlasJsonExArray arrayGraph = Assert.IsType<AtlasJsonExArray>(
            arrayResult.Graph);
        Assert.Same(
            arrayGraph,
            Assert.IsType<AtlasJsonExReference>(Assert.Single(arrayGraph.Elements)).Target);
        Assert.True(arrayResult.GraphCensus.Cycles > 0);
    }

    [Theory]
    [InlineData("{\"@r\":1,\"x\":0}", AtlasSaveReadFailure.InvalidArrayOrReferenceWrapper)]
    [InlineData("{\"@c\":1,\"@a\":{}}", AtlasSaveReadFailure.InvalidArrayOrReferenceWrapper)]
    [InlineData("{\"@\":\"Type\"}", AtlasSaveReadFailure.InvalidMarkerType)]
    [InlineData("{\"@c\":\"1\"}", AtlasSaveReadFailure.InvalidMarkerType)]
    [InlineData("{\"@c\":-0}", AtlasSaveReadFailure.InvalidMarkerType)]
    [InlineData("{\"@c\":1.0}", AtlasSaveReadFailure.InvalidMarkerType)]
    [InlineData("{\"@c\":1e0}", AtlasSaveReadFailure.InvalidMarkerType)]
    [InlineData("{\"@c\":2147483648}", AtlasSaveReadFailure.InvalidMarkerType)]
    [InlineData("{\"@c\":1,\"@\":\"\"}", AtlasSaveReadFailure.InvalidMarkerType)]
    [InlineData("{\"@r\":9}", AtlasSaveReadFailure.DanglingReference)]
    public void JsonExRejectsInvalidMarkersAndWrappers(
        string json,
        AtlasSaveReadFailure expected)
    {
        AtlasSaveReadException exception = Assert.Throws<AtlasSaveReadException>(
            () => ReadJson(json));

        Assert.Equal(expected, exception.Failure);
    }

    [Fact]
    public void JsonExRejectsDuplicateReservedMarkersAndIdentities()
    {
        AtlasSaveReadException duplicateMarker = Assert.Throws<AtlasSaveReadException>(
            () => ReadJson("{\"@c\":1,\"@c\":2}"));
        Assert.Equal(AtlasSaveReadFailure.InvalidMarkerType, duplicateMarker.Failure);

        AtlasSaveReadException duplicateIdentity = Assert.Throws<AtlasSaveReadException>(
            () => ReadJson("[{\"@c\":1},{\"@c\":1}]"));
        Assert.Equal(AtlasSaveReadFailure.DuplicateIdentity, duplicateIdentity.Failure);
    }

    [Theory]
    [InlineData("encoded")]
    [InlineData("decompressed")]
    [InlineData("depth")]
    [InlineData("tokens")]
    [InlineData("scalar")]
    [InlineData("nodes")]
    [InlineData("identities")]
    [InlineData("references")]
    public void ReaderLimitsAreInjectableAndClassified(string limit)
    {
        string json = limit switch
        {
            "depth" => "[[[[0]]]]",
            "tokens" => "[1,2,3]",
            "scalar" => "\"abcdef\"",
            "nodes" => "[1]",
            "identities" => "[{\"@c\":1},{\"@c\":2}]",
            "references" =>
                "[{\"@c\":1},{\"@r\":1},{\"@r\":1}]",
            _ => "{\"value\":\"synthetic\"}",
        };
        byte[] encoded = AtlasLzStringCodec.CompressToBase64(
            json,
            cancellationToken: TestContext.Current.CancellationToken);
        AtlasSaveReaderLimits limits = limit switch
        {
            "encoded" => new() { MaximumEncodedBytes = encoded.Length - 1 },
            "decompressed" => new()
            {
                MaximumDecompressedCodeUnits = json.Length - 1,
            },
            "depth" => new() { MaximumJsonDepth = 2 },
            "tokens" => new() { MaximumJsonTokens = 3 },
            "scalar" => new() { MaximumScalarCodeUnits = 3 },
            "nodes" => new() { MaximumGraphNodes = 1 },
            "identities" => new() { MaximumIdentityDefinitions = 1 },
            "references" => new() { MaximumReferenceOccurrences = 1 },
            _ => throw new InvalidOperationException(),
        };

        AtlasSaveReadException exception = Assert.Throws<AtlasSaveReadException>(
            () => AtlasSaveReader.Read(
                encoded,
                limits,
                TestContext.Current.CancellationToken));

        AtlasSaveReadFailure expected = limit switch
        {
            "encoded" => AtlasSaveReadFailure.EncodedInputLimit,
            "decompressed" => AtlasSaveReadFailure.DecompressedSizeLimit,
            "depth" => AtlasSaveReadFailure.JsonDepthLimit,
            "tokens" => AtlasSaveReadFailure.JsonTokenLimit,
            "scalar" => AtlasSaveReadFailure.ScalarSizeLimit,
            "nodes" => AtlasSaveReadFailure.GraphNodeLimit,
            "identities" => AtlasSaveReadFailure.IdentityCountLimit,
            "references" => AtlasSaveReadFailure.ReferenceCountLimit,
            _ => throw new InvalidOperationException(),
        };
        Assert.Equal(expected, exception.Failure);
    }

    [Fact]
    public void ReferenceWrappersConsumeTheGraphNodeLimit()
    {
        const string json =
            "[{\"@c\":1},{\"@r\":1},{\"@r\":1},{\"@r\":1},{\"@r\":1}]";
        byte[] encoded = AtlasLzStringCodec.CompressToBase64(
            json,
            cancellationToken: TestContext.Current.CancellationToken);
        AtlasSaveReaderLimits limits = new()
        {
            MaximumGraphNodes = 4,
            MaximumReferenceOccurrences = 10,
        };

        AtlasSaveReadException exception = Assert.Throws<AtlasSaveReadException>(
            () => AtlasSaveReader.Read(
                encoded,
                limits,
                TestContext.Current.CancellationToken));

        Assert.Equal(AtlasSaveReadFailure.GraphNodeLimit, exception.Failure);
    }

    [Theory]
    [InlineData("property")]
    [InlineData("string")]
    [InlineData("number")]
    public void RawScalarBoundsPrecedeLexemeMaterialization(string kind)
    {
        const int limit = 64;
        string escaped = string.Concat(Enumerable.Repeat("\\u0061", limit + 1));
        string json = kind switch
        {
            "property" => $"{{\"{escaped}\":0}}",
            "string" => $"\"{escaped}\"",
            "number" => new string('1', limit + 1),
            _ => throw new InvalidOperationException(),
        };
        byte[] encoded = AtlasLzStringCodec.CompressToBase64(
            json,
            cancellationToken: TestContext.Current.CancellationToken);

        AtlasSaveReadException exception = Assert.Throws<AtlasSaveReadException>(
            () => AtlasSaveReader.Read(
                encoded,
                new AtlasSaveReaderLimits { MaximumScalarCodeUnits = limit },
                TestContext.Current.CancellationToken));

        Assert.Equal(AtlasSaveReadFailure.ScalarSizeLimit, exception.Failure);
    }

    [Theory]
    [InlineData("property")]
    [InlineData("string")]
    public void HighlyEscapedOversizedScalarIsRejectedWithBoundedAllocation(string kind)
    {
        const int repetitions = 128 * 1024;
        string escaped = string.Concat(Enumerable.Repeat("\\u0061", repetitions));
        string json = kind switch
        {
            "property" => $"{{\"{escaped}\":0}}",
            "string" => $"\"{escaped}\"",
            _ => throw new InvalidOperationException(),
        };
        byte[] encoded = AtlasLzStringCodec.CompressToBase64(
            json,
            cancellationToken: TestContext.Current.CancellationToken);
        _ = Assert.Throws<AtlasSaveReadException>(
            () => AtlasSaveReader.Read(
                AtlasLzStringCodec.CompressToBase64(
                    "\"\\u0061\"",
                    cancellationToken: TestContext.Current.CancellationToken),
                new AtlasSaveReaderLimits { MaximumScalarCodeUnits = 1 },
                TestContext.Current.CancellationToken));

        long before = GC.GetAllocatedBytesForCurrentThread();
        AtlasSaveReadException exception = Assert.Throws<AtlasSaveReadException>(
            () => AtlasSaveReader.Read(
                encoded,
                new AtlasSaveReaderLimits { MaximumScalarCodeUnits = 128 },
                TestContext.Current.CancellationToken));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(AtlasSaveReadFailure.ScalarSizeLimit, exception.Failure);
        Assert.True(
            allocated < json.Length * 12L,
            $"Oversized scalar rejection allocated {allocated} bytes.");
    }

    [Fact]
    public void LargeNumericTokenIsRejectedWithBoundedAllocation()
    {
        string json = new('7', 512 * 1024);
        byte[] encoded = AtlasLzStringCodec.CompressToBase64(
            json,
            cancellationToken: TestContext.Current.CancellationToken);

        long before = GC.GetAllocatedBytesForCurrentThread();
        AtlasSaveReadException exception = Assert.Throws<AtlasSaveReadException>(
            () => AtlasSaveReader.Read(
                encoded,
                new AtlasSaveReaderLimits { MaximumScalarCodeUnits = 128 },
                TestContext.Current.CancellationToken));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(AtlasSaveReadFailure.ScalarSizeLimit, exception.Failure);
        Assert.True(
            allocated < json.Length * 12L,
            $"Oversized numeric rejection allocated {allocated} bytes.");
    }

    [Fact]
    public async Task ReaderStreamAndAllMajorPhasesObserveCancellation()
    {
        byte[] encoded = AtlasLzStringCodec.CompressToBase64(
            "{\"@c\":1,\"self\":{\"@r\":1}}",
            cancellationToken: TestContext.Current.CancellationToken);
        using CancellationTokenSource source = new();
        await source.CancelAsync();
        Assert.ThrowsAny<OperationCanceledException>(
            () => AtlasSaveReader.Read(encoded, cancellationToken: source.Token));

        await using MemoryStream stream = new(encoded);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AtlasSaveReader.ReadAsync(stream, cancellationToken: source.Token).AsTask());
    }

    [Fact]
    public void SemanticNoOpReturnsExactOriginalCompressedBytes()
    {
        byte[] encoded = AtlasLzStringCodec.CompressToBase64(
            "{\"value\":1,\"value\":1.0}",
            cancellationToken: TestContext.Current.CancellationToken);

        AtlasSaveReadResult result = AtlasSaveReader.Read(
            encoded,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(encoded, result.GetSemanticNoOpBytes());
        Assert.Equal(encoded, result.OriginalCompressedBytes.ToArray());
    }

    [Fact]
    public void ReaderFailuresDoNotDiscloseScalarContent()
    {
        const string privateScalar = "synthetic-private-scalar";
        byte[] encoded = AtlasLzStringCodec.CompressToBase64(
            $"{{\"@c\":\"{privateScalar}\"}}",
            cancellationToken: TestContext.Current.CancellationToken);

        AtlasSaveReadException exception = Assert.Throws<AtlasSaveReadException>(
            () => AtlasSaveReader.Read(
                encoded,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.DoesNotContain(privateScalar, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedJsonAndCompressedInputAreClassifiedSeparately()
    {
        byte[] malformedJson = AtlasLzStringCodec.CompressToBase64(
            "{",
            cancellationToken: TestContext.Current.CancellationToken);
        AtlasSaveReadException jsonException = Assert.Throws<AtlasSaveReadException>(
            () => AtlasSaveReader.Read(
                malformedJson,
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(AtlasSaveReadFailure.MalformedJson, jsonException.Failure);

        AtlasSaveReadException compressedException = Assert.Throws<AtlasSaveReadException>(
            () => AtlasSaveReader.Read(
                "QAAA"u8.ToArray(),
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(
            AtlasSaveReadFailure.MalformedOrTruncatedCompressedInput,
            compressedException.Failure);

        foreach (string invalidSurrogateJson in
                 new[] { "{\"\\uD800\":0}", "{\"value\":\"\\uD800\"}" })
        {
            AtlasSaveReadException surrogateException =
                Assert.Throws<AtlasSaveReadException>(
                    () => ReadJson(invalidSurrogateJson));
            Assert.Equal(
                AtlasSaveReadFailure.MalformedJson,
                surrogateException.Failure);
        }
    }

    private static AtlasSaveReadResult ReadJson(string json) =>
        AtlasSaveReader.Read(
            AtlasLzStringCodec.CompressToBase64(
                json,
                cancellationToken: TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);
}
