using System.Reflection;
using System.Text;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class AtlasGoldMutationKernelTests
{
    public static TheoryData<AtlasGoldMutationFailure, string> FixedFailures =>
        new()
        {
            {
                AtlasGoldMutationFailure.SourceIncomplete,
                "The Gold source is incomplete."
            },
            {
                AtlasGoldMutationFailure.SourceDisagrees,
                "The Gold source candidates disagree."
            },
            {
                AtlasGoldMutationFailure.InvalidSourceSpan,
                "A Gold source span is invalid."
            },
            {
                AtlasGoldMutationFailure.OverlappingSourceSpans,
                "Gold source spans overlap."
            },
            {
                AtlasGoldMutationFailure.CandidateLimitExceeded,
                "The Gold candidate exceeds the configured limits."
            },
            {
                AtlasGoldMutationFailure.CandidateVerificationFailed,
                "The Gold candidate could not be verified."
            },
            {
                AtlasGoldMutationFailure.UnsupportedInternalState,
                "The Gold mutation kernel reached an unsupported internal state."
            },
        };

    public static TheoryData<string, int> InvalidLimits =>
        new()
        {
            { "encoded", 0 },
            { "encoded", -1 },
            { "decompressed", 0 },
            { "decompressed", -1 },
            { "depth", 0 },
            { "depth", -1 },
            { "tokens", 0 },
            { "tokens", -1 },
            { "scalar", 0 },
            { "scalar", -1 },
            { "nodes", 0 },
            { "nodes", -1 },
            { "identities", 0 },
            { "identities", -1 },
            { "references", 0 },
            { "references", -1 },
        };

    public static TheoryData<long> RepresentativeValues =>
        new()
        {
            0,
            -7,
            1_000_000_000_000,
            long.MinValue,
            long.MaxValue,
        };

    public static IEnumerable<object[]> IncompleteSources()
    {
        string party = Party("7");
        string variables = Variables("7");

        yield return [Root(variables)];
        yield return [Root(party, party, variables)];
        yield return [Root(Property("party", "[]"), variables)];
        yield return [Root(Party("\"text\""), variables)];
        yield return [Root(Party("1.0"), variables)];
        yield return [Root(Party("1e2"), variables)];
        yield return [Root(Party("9223372036854775808"), variables)];
        yield return [Root(party, Property("variables", "{}"))];
        yield return [Root(party, Variables("7", elementCount: 215))];
    }

    public static TheoryData<AtlasSaveReadFailure> ReaderLimitFailures =>
        new()
        {
            AtlasSaveReadFailure.EncodedInputLimit,
            AtlasSaveReadFailure.DecompressedSizeLimit,
            AtlasSaveReadFailure.JsonDepthLimit,
            AtlasSaveReadFailure.JsonTokenLimit,
            AtlasSaveReadFailure.ScalarSizeLimit,
            AtlasSaveReadFailure.GraphNodeLimit,
            AtlasSaveReadFailure.IdentityCountLimit,
            AtlasSaveReadFailure.ReferenceCountLimit,
        };

    public static TheoryData<AtlasSaveReadFailure> ReaderVerificationFailures =>
        new()
        {
            AtlasSaveReadFailure.InvalidCompressedAlphabetOrPadding,
            AtlasSaveReadFailure.MalformedOrTruncatedCompressedInput,
            AtlasSaveReadFailure.MalformedJson,
            AtlasSaveReadFailure.DuplicateIdentity,
            AtlasSaveReadFailure.DanglingReference,
            AtlasSaveReadFailure.InvalidMarkerType,
            AtlasSaveReadFailure.InvalidArrayOrReferenceWrapper,
        };

    public static IEnumerable<object[]> IntegerLexemes()
    {
        yield return ["0", true, 0L];
        yield return ["-0", true, 0L];
        yield return ["1", true, 1L];
        yield return ["-7", true, -7L];
        yield return ["9223372036854775807", true, long.MaxValue];
        yield return ["-9223372036854775808", true, long.MinValue];
        yield return ["", false, 0L];
        yield return ["-", false, 0L];
        yield return ["+1", false, 0L];
        yield return ["01", false, 0L];
        yield return ["-01", false, 0L];
        yield return ["1.0", false, 0L];
        yield return ["1e2", false, 0L];
        yield return ["9223372036854775808", false, 0L];
        yield return ["-9223372036854775809", false, 0L];
    }

    [Fact]
    public void PublicContractIsClosedImmutableAndExactlyShaped()
    {
        Assert.Equal(
            [
                nameof(AtlasGoldMutationDisposition.Unchanged),
                nameof(AtlasGoldMutationDisposition.Changed),
            ],
            Enum.GetNames<AtlasGoldMutationDisposition>());
        Assert.Equal(
            [
                nameof(AtlasGoldMutationFailure.SourceIncomplete),
                nameof(AtlasGoldMutationFailure.SourceDisagrees),
                nameof(AtlasGoldMutationFailure.InvalidSourceSpan),
                nameof(AtlasGoldMutationFailure.OverlappingSourceSpans),
                nameof(AtlasGoldMutationFailure.CandidateLimitExceeded),
                nameof(AtlasGoldMutationFailure.CandidateVerificationFailed),
                nameof(AtlasGoldMutationFailure.UnsupportedInternalState),
            ],
            Enum.GetNames<AtlasGoldMutationFailure>());

        Type exceptionType = typeof(AtlasGoldMutationException);
        Assert.True(exceptionType.IsSealed);
        Assert.Empty(exceptionType.GetConstructors());
        PropertyInfo exceptionFailure = Assert.Single(
            exceptionType.GetProperties(
                BindingFlags.Public
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly));
        Assert.Equal(nameof(AtlasGoldMutationException.Failure), exceptionFailure.Name);
        Assert.Equal(typeof(AtlasGoldMutationFailure), exceptionFailure.PropertyType);
        Assert.Null(exceptionFailure.SetMethod);

        Type resultType = typeof(AtlasGoldMutationResult);
        Assert.True(resultType.IsSealed);
        Assert.Empty(resultType.GetConstructors());
        PropertyInfo disposition = Assert.Single(
            resultType.GetProperties(
                BindingFlags.Public
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly));
        Assert.Equal(nameof(AtlasGoldMutationResult.Disposition), disposition.Name);
        Assert.Equal(typeof(AtlasGoldMutationDisposition), disposition.PropertyType);
        Assert.Null(disposition.SetMethod);
        MethodInfo getter = Assert.Single(
            resultType.GetMethods(
                BindingFlags.Public
                    | BindingFlags.Instance
                    | BindingFlags.DeclaredOnly),
            static method => !method.IsSpecialName);
        Assert.Equal(nameof(AtlasGoldMutationResult.GetCompressedBytes), getter.Name);
        Assert.Equal(typeof(byte[]), getter.ReturnType);
        ParameterInfo getterCancellation = Assert.Single(getter.GetParameters());
        Assert.Equal(typeof(CancellationToken), getterCancellation.ParameterType);
        Assert.True(getterCancellation.HasDefaultValue);

        MethodInfo create = Assert.Single(
            typeof(AtlasGoldMutationKernel).GetMethods(
                BindingFlags.Public
                    | BindingFlags.Static
                    | BindingFlags.DeclaredOnly));
        Assert.Equal(nameof(AtlasGoldMutationKernel.CreateCandidate), create.Name);
        Assert.Equal(resultType, create.ReturnType);
        ParameterInfo[] parameters = create.GetParameters();
        Assert.Equal(
            [
                typeof(AtlasSaveReadResult),
                typeof(long),
                typeof(AtlasSaveReaderLimits),
                typeof(CancellationToken),
            ],
            parameters.Select(static parameter => parameter.ParameterType));
        Assert.True(parameters[^1].HasDefaultValue);
    }

    [Theory]
    [MemberData(nameof(FixedFailures))]
    public void EveryFailureUsesFixedValueFreeText(
        AtlasGoldMutationFailure failure,
        string expectedMessage)
    {
        const string candidateValue = "9223372036854775807";

        AtlasGoldMutationException exception = new(failure);

        Assert.Equal(failure, exception.Failure);
        Assert.Equal(expectedMessage, exception.Message);
        Assert.DoesNotContain(candidateValue, exception.Message, StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void NullArgumentsUseFixedArgumentFailures()
    {
        AtlasSaveReadResult source = ReadSource(
            Root(Party("1"), Variables("1")));

        ArgumentNullException nullSource = Assert.Throws<ArgumentNullException>(
            () => AtlasGoldMutationKernel.CreateCandidate(
                null!,
                2,
                AtlasSaveReaderLimits.Default,
                TestContext.Current.CancellationToken));
        Assert.Equal("source", nullSource.ParamName);
        Assert.Contains(
            "The Atlas save read result is required.",
            nullSource.Message,
            StringComparison.Ordinal);

        ArgumentNullException nullLimits = Assert.Throws<ArgumentNullException>(
            () => AtlasGoldMutationKernel.CreateCandidate(
                source,
                2,
                null!,
                TestContext.Current.CancellationToken));
        Assert.Equal("limits", nullLimits.ParamName);
        Assert.Contains(
            "The Atlas save reader limits are required.",
            nullLimits.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidLimits))]
    public void InvalidLimitsPrecedeCancellationInspectionAndMutation(
        string limitName,
        int invalidValue)
    {
        AtlasSaveReadResult disagreeingSource = ReadSource(
            Root(Party("1"), Variables("2")));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        ArgumentOutOfRangeException exception =
            Assert.Throws<ArgumentOutOfRangeException>(
                () => AtlasGoldMutationKernel.CreateCandidate(
                    disagreeingSource,
                    3,
                    CreateInvalidLimits(limitName, invalidValue),
                    cancellation.Token));

        Assert.Equal("AtlasSaveReaderLimits", exception.ParamName);
        Assert.Contains(
            "Reader limits must be positive.",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SemanticNoOpPreservesExactCompressedBytesAndNegativeZero()
    {
        string json = Root(
            Property("unknown", "\"synthetic\""),
            Party("-0"),
            Variables("-0"));
        byte[] compressed = Compress(json);
        AtlasSaveReadResult source = AtlasSaveReader.Read(
            compressed,
            cancellationToken: TestContext.Current.CancellationToken);
        AtlasSaveReaderLimits mutationIncompatibleLimits = new()
        {
            MaximumEncodedBytes = 1,
            MaximumDecompressedCodeUnits = 1,
        };

        AtlasGoldMutationResult result = AtlasGoldMutationKernel.CreateCandidate(
            source,
            0,
            mutationIncompatibleLimits,
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasGoldMutationDisposition.Unchanged, result.Disposition);
        byte[] first = result.GetCompressedBytes(TestContext.Current.CancellationToken);
        byte[] second = result.GetCompressedBytes(TestContext.Current.CancellationToken);
        Assert.Equal(compressed, first);
        Assert.Equal(compressed, second);
        Assert.NotSame(first, second);
        first[0] ^= 0x7f;
        Assert.Equal(
            compressed,
            result.GetCompressedBytes(TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(RepresentativeValues))]
    public void ChangedCandidatesSupportRepresentativeInt64Values(long value)
    {
        AtlasSaveReadResult source = ReadSource(
            Root(Party("41"), Variables("41")));

        AtlasGoldMutationResult result = AtlasGoldMutationKernel.CreateCandidate(
            source,
            value,
            AtlasSaveReaderLimits.Default,
            TestContext.Current.CancellationToken);
        AtlasSaveReadResult candidate = AtlasSaveReader.Read(
            result.GetCompressedBytes(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);
        AtlasGoldReadModelResult gold = AtlasGoldReadModel.Read(
            candidate,
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasGoldMutationDisposition.Changed, result.Disposition);
        Assert.Equal(AtlasGoldAggregateState.Consistent, gold.Aggregate);
        Assert.Equal(value, gold.PartyGold.Value);
        Assert.Equal(value, gold.VariableGold.Value);
    }

    [Theory]
    [InlineData("12345", 7L)]
    [InlineData("7", 1234567890123456789L)]
    public void ReplacementsMayBeShorterOrLongerAndPreserveEveryOtherUtf8Byte(
        string currentLexeme,
        long value)
    {
        string prefix =
            "{\n  \"unknown\":\"雪😀\",\"unrelated\":1e+2,"
            + "\"party\" : {\"before\":true,\"_gold\" : ";
        string middle =
            ",\"after\":-0},\n  \"variables\":{\"_data\":";
        string suffix =
            ",\"tail\":0.50},\"order\":\"last\"\n}";
        string json = prefix
            + currentLexeme
            + middle
            + DataArray(currentLexeme)
            + suffix;
        string replacement = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string expected = prefix
            + replacement
            + middle
            + DataArray(replacement)
            + suffix;
        AtlasSaveReadResult source = ReadSource(json);

        AtlasGoldMutationResult result = AtlasGoldMutationKernel.CreateCandidate(
            source,
            value,
            AtlasSaveReaderLimits.Default,
            TestContext.Current.CancellationToken);
        byte[] candidateBytes = result.GetCompressedBytes(
            TestContext.Current.CancellationToken);
        AtlasSaveReadResult candidate = AtlasSaveReader.Read(
            candidateBytes,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(Encoding.UTF8.GetBytes(expected), candidate.Json.Utf8Source.ToArray());
        Assert.Equal(
            expected,
            AtlasLzStringCodec.DecompressFromBase64(
                candidateBytes,
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.Contains("1e+2", expected, StringComparison.Ordinal);
        Assert.Contains("-0", expected, StringComparison.Ordinal);
        Assert.Contains("0.50", expected, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceBackedObjectsAndArraysMutateResolvedScalarDefinitions()
    {
        string json = ReferenceBackedGoldJson("53");
        string expected = ReferenceBackedGoldJson("-91");
        AtlasSaveReadResult source = ReadSource(json);

        AtlasGoldMutationResult result = AtlasGoldMutationKernel.CreateCandidate(
            source,
            -91,
            AtlasSaveReaderLimits.Default,
            TestContext.Current.CancellationToken);
        AtlasSaveReadResult candidate = AtlasSaveReader.Read(
            result.GetCompressedBytes(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(Encoding.UTF8.GetBytes(expected), candidate.Json.Utf8Source.ToArray());
        AtlasGoldReadModelResult gold = AtlasGoldReadModel.Read(
            candidate,
            TestContext.Current.CancellationToken);
        Assert.Equal(-91, gold.PartyGold.Value);
        Assert.Equal(-91, gold.VariableGold.Value);
    }

    [Theory]
    [MemberData(nameof(IncompleteSources))]
    public void EveryRepresentativeIncompleteSubclassIsRefused(string json)
    {
        AtlasGoldMutationException exception =
            Assert.Throws<AtlasGoldMutationException>(
                () => AtlasGoldMutationKernel.CreateCandidate(
                    ReadSource(json),
                    8,
                    AtlasSaveReaderLimits.Default,
                    TestContext.Current.CancellationToken));

        Assert.Equal(AtlasGoldMutationFailure.SourceIncomplete, exception.Failure);
    }

    [Fact]
    public void DisagreeingPresentCandidatesAreRefused()
    {
        AtlasGoldMutationException exception =
            Assert.Throws<AtlasGoldMutationException>(
                () => AtlasGoldMutationKernel.CreateCandidate(
                    ReadSource(Root(Party("7"), Variables("8"))),
                    9,
                    AtlasSaveReaderLimits.Default,
                    TestContext.Current.CancellationToken));

        Assert.Equal(AtlasGoldMutationFailure.SourceDisagrees, exception.Failure);
    }

    [Theory]
    [MemberData(nameof(IntegerLexemes))]
    public void IntegerGrammarHelperMatchesTheReleasedA6Grammar(
        string lexeme,
        bool expectedSuccess,
        long expectedValue)
    {
        bool success = AtlasGoldMutationKernel.TryParseIntegerLexeme(
            Encoding.ASCII.GetBytes(lexeme),
            TestContext.Current.CancellationToken,
            out long value);

        Assert.Equal(expectedSuccess, success);
        Assert.Equal(expectedSuccess ? expectedValue : 0, value);
    }

    [Fact]
    public void SpanNormalizationDeduplicatesEqualsAndSortsDistinctSpans()
    {
        AtlasGoldNormalizedSpanSet equal =
            AtlasGoldMutationKernel.NormalizeSourceSpans(
                "7"u8.ToArray(),
                7,
                new AtlasJsonSourceSpan(0, 1),
                new AtlasJsonSourceSpan(0, 1),
                TestContext.Current.CancellationToken);
        Assert.Equal(1, equal.Count);
        Assert.Null(equal.Second);

        AtlasGoldNormalizedSpanSet distinct =
            AtlasGoldMutationKernel.NormalizeSourceSpans(
                "7 7"u8.ToArray(),
                7,
                new AtlasJsonSourceSpan(2, 1),
                new AtlasJsonSourceSpan(0, 1),
                TestContext.Current.CancellationToken);
        Assert.Equal(2, distinct.Count);
        Assert.Equal(0, distinct.First.Start);
        Assert.Equal(2, distinct.Second!.Value.Start);
    }

    [Fact]
    public void InvalidSpanTakesPrecedenceBeforeOverlapClassification()
    {
        AtlasGoldMutationException exception =
            Assert.Throws<AtlasGoldMutationException>(
                () => AtlasGoldMutationKernel.NormalizeSourceSpans(
                    "12"u8.ToArray(),
                    1,
                    new AtlasJsonSourceSpan(0, 1),
                    new AtlasJsonSourceSpan(0, 2),
                    TestContext.Current.CancellationToken));

        Assert.Equal(AtlasGoldMutationFailure.InvalidSourceSpan, exception.Failure);
    }

    [Theory]
    [InlineData(-1L, 1L)]
    [InlineData(0L, 0L)]
    [InlineData(0L, 2L)]
    [InlineData(long.MaxValue, 1L)]
    public void MalformedOrOutOfRangeSpansAreInvalid(long start, long length)
    {
        AtlasGoldMutationException exception =
            Assert.Throws<AtlasGoldMutationException>(
                () => AtlasGoldMutationKernel.NormalizeSourceSpan(
                    "7"u8.ToArray(),
                    7,
                    new AtlasJsonSourceSpan(start, length),
                    TestContext.Current.CancellationToken));

        Assert.Equal(AtlasGoldMutationFailure.InvalidSourceSpan, exception.Failure);
    }

    [Theory]
    [InlineData("8", 7L)]
    [InlineData("+7", 7L)]
    [InlineData("07", 7L)]
    [InlineData("7.0", 7L)]
    [InlineData("7e0", 7L)]
    public void MismatchedOrNonIntegerSourceSlicesAreInvalid(
        string lexeme,
        long expectedValue)
    {
        AtlasGoldMutationException exception =
            Assert.Throws<AtlasGoldMutationException>(
                () => AtlasGoldMutationKernel.NormalizeSourceSpan(
                    Encoding.ASCII.GetBytes(lexeme),
                    expectedValue,
                    new AtlasJsonSourceSpan(0, lexeme.Length),
                    TestContext.Current.CancellationToken));

        Assert.Equal(AtlasGoldMutationFailure.InvalidSourceSpan, exception.Failure);
    }

    [Fact]
    public void IndividuallyValidDistinctSpansAreRefusedWhenTheyOverlap()
    {
        ReadOnlyMemory<byte> source = "123"u8.ToArray();
        AtlasGoldNormalizedSpan first =
            AtlasGoldMutationKernel.NormalizeSourceSpan(
                source,
                12,
                new AtlasJsonSourceSpan(0, 2),
                TestContext.Current.CancellationToken);
        AtlasGoldNormalizedSpan second =
            AtlasGoldMutationKernel.NormalizeSourceSpan(
                source,
                23,
                new AtlasJsonSourceSpan(1, 2),
                TestContext.Current.CancellationToken);
        Assert.Equal(
            AtlasGoldSpanRelationship.Overlapping,
            AtlasGoldMutationKernel.ClassifySpanPair(first, second));

        AtlasGoldMutationException exception =
            Assert.Throws<AtlasGoldMutationException>(
                () => AtlasGoldMutationKernel.CreateNormalizedSpanSet(
                    first,
                    second));
        Assert.Equal(
            AtlasGoldMutationFailure.OverlappingSourceSpans,
            exception.Failure);
    }

    [Fact]
    public void EncodedDecompressedAndScalarLimitsAreMappedEndToEnd()
    {
        AtlasSaveReadResult source = ReadSource(
            Root(Party("1"), Variables("1")));
        AtlasSaveReaderLimits[] limits =
        [
            new() { MaximumEncodedBytes = 4 },
            new() { MaximumDecompressedCodeUnits = 8 },
            new() { MaximumScalarCodeUnits = 1 },
        ];

        foreach (AtlasSaveReaderLimits limit in limits)
        {
            AtlasGoldMutationException exception =
                Assert.Throws<AtlasGoldMutationException>(
                    () => AtlasGoldMutationKernel.CreateCandidate(
                        source,
                        long.MaxValue,
                        limit,
                        TestContext.Current.CancellationToken));
            Assert.Equal(
                AtlasGoldMutationFailure.CandidateLimitExceeded,
                exception.Failure);
        }
    }

    [Theory]
    [MemberData(nameof(ReaderLimitFailures))]
    public void EveryReaderLimitFailureMapsToCandidateLimit(
        AtlasSaveReadFailure failure)
    {
        Assert.Equal(
            AtlasGoldMutationFailure.CandidateLimitExceeded,
            AtlasGoldMutationKernel.MapReaderFailure(failure));
    }

    [Theory]
    [MemberData(nameof(ReaderVerificationFailures))]
    public void EveryNonLimitReaderParseFailureMapsToVerification(
        AtlasSaveReadFailure failure)
    {
        Assert.Equal(
            AtlasGoldMutationFailure.CandidateVerificationFailed,
            AtlasGoldMutationKernel.MapReaderFailure(failure));
    }

    [Fact]
    public void CodecAndUnsupportedFailureMappingsAreClosed()
    {
        Assert.Equal(
            AtlasGoldMutationFailure.CandidateLimitExceeded,
            AtlasGoldMutationKernel.MapCodecFailure(
                AtlasLzStringFailure.EncodedInputLimit));
        Assert.Equal(
            AtlasGoldMutationFailure.CandidateLimitExceeded,
            AtlasGoldMutationKernel.MapCodecFailure(
                AtlasLzStringFailure.DecompressedSizeLimit));
        Assert.Equal(
            AtlasGoldMutationFailure.CandidateVerificationFailed,
            AtlasGoldMutationKernel.MapCodecFailure(
                AtlasLzStringFailure.MalformedOrTruncated));
        Assert.Equal(
            AtlasGoldMutationFailure.UnsupportedInternalState,
            AtlasGoldMutationKernel.MapCodecFailure(
                AtlasLzStringFailure.UnsupportedState));
        Assert.Equal(
            AtlasGoldMutationFailure.UnsupportedInternalState,
            AtlasGoldMutationKernel.MapReaderFailure(
                AtlasSaveReadFailure.UnsupportedInternalState));
    }

    [Fact]
    public void StrictUtf8AndCodeUnitLimitsAreEnforcedBeforeCompression()
    {
        AtlasGoldMutationException invalidUtf8 =
            Assert.Throws<AtlasGoldMutationException>(
                () => AtlasGoldMutationKernel.DecodeCandidateUtf8(
                    new byte[] { 0xff },
                    AtlasSaveReaderLimits.Default,
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            AtlasGoldMutationFailure.CandidateVerificationFailed,
            invalidUtf8.Failure);

        AtlasGoldMutationException codeUnitLimit =
            Assert.Throws<AtlasGoldMutationException>(
                () => AtlasGoldMutationKernel.DecodeCandidateUtf8(
                    Encoding.UTF8.GetBytes("😀"),
                    new AtlasSaveReaderLimits
                    {
                        MaximumDecompressedCodeUnits = 1,
                    },
                    TestContext.Current.CancellationToken));
        Assert.Equal(
            AtlasGoldMutationFailure.CandidateLimitExceeded,
            codeUnitLimit.Failure);
    }

    [Fact]
    public void VerificationRequiresExactLosslessBytesAndExpectedA6Equality()
    {
        AtlasSaveReadResult candidate = ReadSource(
            Root(Party("9"), Variables("9")));
        ReadOnlyMemory<byte> exact = candidate.Json.Utf8Source;

        Assert.Null(
            AtlasGoldMutationKernel.VerifyCandidate(
                exact,
                candidate,
                9,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            AtlasGoldMutationFailure.CandidateVerificationFailed,
            AtlasGoldMutationKernel.VerifyCandidate(
                "{}"u8.ToArray(),
                candidate,
                9,
                TestContext.Current.CancellationToken));
        Assert.Equal(
            AtlasGoldMutationFailure.CandidateVerificationFailed,
            AtlasGoldMutationKernel.VerifyCandidate(
                exact,
                candidate,
                10,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public void ContradictoryTransientInspectionMapsToUnsupportedInternalState()
    {
        AtlasGoldInspectionResult contradictory = new(
            new AtlasGoldCandidateInspection(
                AtlasGoldCandidateResult.Present(1),
                sourceSpan: null),
            new AtlasGoldCandidateInspection(
                AtlasGoldCandidateResult.Present(1),
                new AtlasJsonSourceSpan(0, 1)));

        Assert.Equal(
            AtlasGoldMutationFailure.UnsupportedInternalState,
            AtlasGoldMutationKernel.ClassifySource(
                contradictory,
                out _));
        Assert.Equal(
            AtlasGoldMutationFailure.UnsupportedInternalState,
            AtlasGoldMutationKernel.VerifyInspection(
                contradictory,
                1));
    }

    [Fact]
    public void MutationDoesNotChangeAnySourceObservation()
    {
        AtlasSaveReadResult source = ReadSource(
            ReferenceBackedGoldJson("31"));
        AtlasJsonExNode graph = source.Graph;
        AtlasJsonExObject root = Assert.IsType<AtlasJsonExObject>(graph);
        AtlasJsonExNode[] memberValues =
            root.Members.Select(static member => member.Value).ToArray();
        byte[] utf8 = source.Json.Utf8Source.ToArray();
        byte[] compressed = source.OriginalCompressedBytes.ToArray();
        byte[] semanticNoOp = source.GetSemanticNoOpBytes();
        AtlasTokenCensus tokens = source.TokenCensus;
        AtlasGraphCensus graphCensus = source.GraphCensus;

        AtlasGoldMutationResult result = AtlasGoldMutationKernel.CreateCandidate(
            source,
            32,
            AtlasSaveReaderLimits.Default,
            TestContext.Current.CancellationToken);

        Assert.Equal(AtlasGoldMutationDisposition.Changed, result.Disposition);
        Assert.Same(graph, source.Graph);
        Assert.Equal(memberValues.Length, root.Members.Count);
        for (int index = 0; index < memberValues.Length; index++)
        {
            Assert.Same(memberValues[index], root.Members[index].Value);
        }

        Assert.Equal(utf8, source.Json.Utf8Source.ToArray());
        Assert.Equal(compressed, source.OriginalCompressedBytes.ToArray());
        Assert.Equal(semanticNoOp, source.GetSemanticNoOpBytes());
        Assert.Equal(tokens, source.TokenCensus);
        Assert.Equal(graphCensus, source.GraphCensus);
    }

    [Fact]
    public void CancellationPropagatesAtEntryGetterAndOwnedHelpers()
    {
        AtlasSaveReadResult source = ReadSource(
            Root(Party("1"), Variables("1")));
        AtlasGoldMutationResult result = AtlasGoldMutationKernel.CreateCandidate(
            source,
            2,
            AtlasSaveReaderLimits.Default,
            TestContext.Current.CancellationToken);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(
            () => AtlasGoldMutationKernel.CreateCandidate(
                source,
                2,
                AtlasSaveReaderLimits.Default,
                cancellation.Token));
        Assert.ThrowsAny<OperationCanceledException>(
            () => result.GetCompressedBytes(cancellation.Token));
        Assert.ThrowsAny<OperationCanceledException>(
            () => AtlasGoldMutationKernel.NormalizeSourceSpans(
                "1 1"u8.ToArray(),
                1,
                new AtlasJsonSourceSpan(0, 1),
                new AtlasJsonSourceSpan(2, 1),
                cancellation.Token));
        Assert.ThrowsAny<OperationCanceledException>(
            () => AtlasGoldMutationKernel.ConstructCandidate(
                "1 1"u8.ToArray(),
                new AtlasGoldNormalizedSpanSet(
                    new AtlasGoldNormalizedSpan(0, 1),
                    new AtlasGoldNormalizedSpan(2, 1)),
                "2"u8.ToArray(),
                cancellation.Token));
        Assert.ThrowsAny<OperationCanceledException>(
            () => AtlasGoldMutationKernel.BytesEqual(
                new byte[128 * 1024],
                new byte[128 * 1024],
                cancellation.Token));
        Assert.ThrowsAny<OperationCanceledException>(
            () => AtlasGoldMutationKernel.DecodeCandidateUtf8(
                "{}"u8.ToArray(),
                AtlasSaveReaderLimits.Default,
                cancellation.Token));
    }

    [Fact]
    public void LargeBoundedDocumentUsesChunkedByteExactConstruction()
    {
        string padding = new('a', 512 * 1024);
        string json = Root(
            Property("padding", $"\"{padding}\""),
            Party("5"),
            Variables("5"),
            Property("tail", "\"synthetic\""));
        string expected = Root(
            Property("padding", $"\"{padding}\""),
            Party("6000000000"),
            Variables("6000000000"),
            Property("tail", "\"synthetic\""));
        AtlasSaveReadResult source = ReadSource(json);

        AtlasGoldMutationResult result = AtlasGoldMutationKernel.CreateCandidate(
            source,
            6_000_000_000,
            AtlasSaveReaderLimits.Default,
            TestContext.Current.CancellationToken);
        AtlasSaveReadResult candidate = AtlasSaveReader.Read(
            result.GetCompressedBytes(TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(Encoding.UTF8.GetBytes(expected), candidate.Json.Utf8Source.ToArray());
    }

    private static AtlasSaveReaderLimits CreateInvalidLimits(
        string limitName,
        int invalidValue) =>
        limitName switch
        {
            "encoded" => new() { MaximumEncodedBytes = invalidValue },
            "decompressed" => new()
            {
                MaximumDecompressedCodeUnits = invalidValue,
            },
            "depth" => new() { MaximumJsonDepth = invalidValue },
            "tokens" => new() { MaximumJsonTokens = invalidValue },
            "scalar" => new() { MaximumScalarCodeUnits = invalidValue },
            "nodes" => new() { MaximumGraphNodes = invalidValue },
            "identities" => new() { MaximumIdentityDefinitions = invalidValue },
            "references" => new() { MaximumReferenceOccurrences = invalidValue },
            _ => throw new InvalidOperationException(),
        };

    private static AtlasSaveReadResult ReadSource(string json) =>
        AtlasSaveReader.Read(
            Compress(json),
            cancellationToken: TestContext.Current.CancellationToken);

    private static byte[] Compress(string json) =>
        AtlasLzStringCodec.CompressToBase64(
            json,
            cancellationToken: TestContext.Current.CancellationToken);

    private static string ReferenceBackedGoldJson(string gold) =>
        Root(
            Property("party", "{\"@r\":1}"),
            Property("variables", "{\"@r\":2}"),
            Property(
                "partyTarget",
                $"{{\"@c\":1,\"note\":\"keep\",\"_gold\":{gold}}}"),
            Property(
                "variablesTarget",
                "{\"@c\":2,\"_data\":{\"@r\":3},\"tail\":1e+2}"),
            Property(
                "dataTarget",
                $"{{\"@c\":3,\"@a\":{DataArray(gold)}}}"));

    private static string Root(params string[] properties) =>
        $"{{{string.Join(",", properties)}}}";

    private static string Property(string name, string value) =>
        $"\"{name}\":{value}";

    private static string Party(string gold) =>
        Property("party", $"{{\"_gold\":{gold}}}");

    private static string Variables(
        string gold,
        int elementCount = 216,
        string filler = "0") =>
        Property(
            "variables",
            $"{{\"_data\":{DataArray(gold, elementCount, filler)}}}");

    private static string DataArray(
        string gold,
        int elementCount = 216,
        string filler = "0")
    {
        StringBuilder builder = new(
            elementCount * (filler.Length + 1) + gold.Length + 2);
        builder.Append('[');
        for (int index = 0; index < elementCount; index++)
        {
            if (index > 0)
            {
                builder.Append(',');
            }

            builder.Append(index == 215 ? gold : filler);
        }

        builder.Append(']');
        return builder.ToString();
    }
}
