using System.Diagnostics;
using System.Reflection;
using System.Text;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class AtlasGoldReadModelTests
{
    public static IEnumerable<object[]> RepresentativeBoundaryCases()
    {
        string party = Party("7");
        string variables = Variables("7");

        yield return
        [
            "non-object root",
            "[]",
            AtlasGoldCandidateState.WrongShape,
            AtlasGoldCandidateState.WrongShape,
        ];
        yield return
        [
            "party missing",
            Root(variables),
            AtlasGoldCandidateState.Missing,
            AtlasGoldCandidateState.Present,
        ];
        yield return
        [
            "party duplicate",
            Root(party, party, variables),
            AtlasGoldCandidateState.Ambiguous,
            AtlasGoldCandidateState.Present,
        ];
        yield return
        [
            "party wrong shape",
            Root(Property("party", "[]"), variables),
            AtlasGoldCandidateState.WrongShape,
            AtlasGoldCandidateState.Present,
        ];
        yield return
        [
            "_gold missing",
            Root(Property("party", "{}"), variables),
            AtlasGoldCandidateState.Missing,
            AtlasGoldCandidateState.Present,
        ];
        yield return
        [
            "_gold duplicate",
            Root(
                Property("party", "{\"_gold\":7,\"_gold\":8}"),
                variables),
            AtlasGoldCandidateState.Ambiguous,
            AtlasGoldCandidateState.Present,
        ];
        yield return
        [
            "_gold wrong shape",
            Root(Party("\"not-a-number\""), variables),
            AtlasGoldCandidateState.WrongShape,
            AtlasGoldCandidateState.Present,
        ];
        yield return
        [
            "variables missing",
            Root(party),
            AtlasGoldCandidateState.Present,
            AtlasGoldCandidateState.Missing,
        ];
        yield return
        [
            "variables duplicate",
            Root(party, variables, variables),
            AtlasGoldCandidateState.Present,
            AtlasGoldCandidateState.Ambiguous,
        ];
        yield return
        [
            "variables wrong shape",
            Root(party, Property("variables", "[]")),
            AtlasGoldCandidateState.Present,
            AtlasGoldCandidateState.WrongShape,
        ];
        yield return
        [
            "_data missing",
            Root(party, Property("variables", "{}")),
            AtlasGoldCandidateState.Present,
            AtlasGoldCandidateState.Missing,
        ];
        yield return
        [
            "_data duplicate",
            Root(
                party,
                Property(
                    "variables",
                    $"{{\"_data\":{DataArray("7")},\"_data\":{DataArray("8")}}}")),
            AtlasGoldCandidateState.Present,
            AtlasGoldCandidateState.Ambiguous,
        ];
        yield return
        [
            "_data wrong shape",
            Root(party, Property("variables", "{\"_data\":{}}")),
            AtlasGoldCandidateState.Present,
            AtlasGoldCandidateState.WrongShape,
        ];
        yield return
        [
            "index missing from short array",
            Root(party, Variables("7", elementCount: 215)),
            AtlasGoldCandidateState.Present,
            AtlasGoldCandidateState.Missing,
        ];
        yield return
        [
            "index stores null",
            Root(party, Variables("null", filler: "null")),
            AtlasGoldCandidateState.Present,
            AtlasGoldCandidateState.WrongShape,
        ];
        yield return
        [
            "index stores text",
            Root(party, Variables("\"not-a-number\"", filler: "null")),
            AtlasGoldCandidateState.Present,
            AtlasGoldCandidateState.WrongShape,
        ];
    }

    [Fact]
    public void EqualAndUnequalPresentCandidatesDeriveTheOnlyCompleteAggregates()
    {
        AtlasGoldReadModelResult consistent = ReadJson(
            Root(Party("41"), Variables("41")));
        AssertCandidate(consistent.PartyGold, AtlasGoldCandidateState.Present, 41);
        AssertCandidate(consistent.VariableGold, AtlasGoldCandidateState.Present, 41);
        Assert.Equal(AtlasGoldAggregateState.Consistent, consistent.Aggregate);
        AssertInvariants(consistent);

        AtlasGoldReadModelResult disagree = ReadJson(
            Root(Party("41"), Variables("42")));
        AssertCandidate(disagree.PartyGold, AtlasGoldCandidateState.Present, 41);
        AssertCandidate(disagree.VariableGold, AtlasGoldCandidateState.Present, 42);
        Assert.Equal(AtlasGoldAggregateState.Disagree, disagree.Aggregate);
        AssertInvariants(disagree);
    }

    [Theory]
    [MemberData(nameof(RepresentativeBoundaryCases))]
    public void RepresentativeFixedPathBoundariesAreClassifiedWithoutPairwiseExpansion(
        string caseName,
        string json,
        AtlasGoldCandidateState expectedPartyState,
        AtlasGoldCandidateState expectedVariableState)
    {
        _ = caseName;

        AtlasGoldReadModelResult result = ReadJson(json);

        AssertCandidate(result.PartyGold, expectedPartyState, ExpectedValue(expectedPartyState));
        AssertCandidate(
            result.VariableGold,
            expectedVariableState,
            ExpectedValue(expectedVariableState));
        Assert.Equal(AtlasGoldAggregateState.Incomplete, result.Aggregate);
        AssertInvariants(result);
    }

    [Theory]
    [InlineData("0", AtlasGoldCandidateState.Present, 0L)]
    [InlineData("-7", AtlasGoldCandidateState.Present, -7L)]
    [InlineData("-0", AtlasGoldCandidateState.Present, 0L)]
    [InlineData("-9223372036854775808", AtlasGoldCandidateState.Present, long.MinValue)]
    [InlineData("9223372036854775807", AtlasGoldCandidateState.Present, long.MaxValue)]
    [InlineData("9223372036854775808", AtlasGoldCandidateState.OutsideInt64, null)]
    [InlineData("-9223372036854775809", AtlasGoldCandidateState.OutsideInt64, null)]
    [InlineData("1.0", AtlasGoldCandidateState.NonInteger, null)]
    [InlineData("1e2", AtlasGoldCandidateState.NonInteger, null)]
    [InlineData("-1E-2", AtlasGoldCandidateState.NonInteger, null)]
    public void ExactNumericLexemesAreClassified(
        string lexeme,
        AtlasGoldCandidateState expectedState,
        long? expectedValue)
    {
        AtlasGoldReadModelResult result = ReadJson(
            Root(Party(lexeme), Variables(lexeme)));

        AssertCandidate(result.PartyGold, expectedState, expectedValue);
        AssertCandidate(result.VariableGold, expectedState, expectedValue);
        Assert.Equal(
            expectedState == AtlasGoldCandidateState.Present
                ? AtlasGoldAggregateState.Consistent
                : AtlasGoldAggregateState.Incomplete,
            result.Aggregate);
        AssertInvariants(result);
    }

    [Fact]
    public void RelevantObjectsAndDataArrayMayBeReferenceBacked()
    {
        string json = Root(
            Property("party", "{\"@r\":1}"),
            Property("variables", "{\"@r\":2}"),
            Property("partyTarget", "{\"@c\":1,\"_gold\":53}"),
            Property("variablesTarget", "{\"@c\":2,\"_data\":{\"@r\":3}}"),
            Property("dataTarget", $"{{\"@c\":3,\"@a\":{DataArray("53")}}}"));

        AtlasGoldReadModelResult result = ReadJson(json);

        AssertCandidate(result.PartyGold, AtlasGoldCandidateState.Present, 53);
        AssertCandidate(result.VariableGold, AtlasGoldCandidateState.Present, 53);
        Assert.Equal(AtlasGoldAggregateState.Consistent, result.Aggregate);
        AssertInvariants(result);
    }

    [Fact]
    public void DuplicateUnrelatedNamesDoNotAffectFixedLookups()
    {
        string json = Root(
            Property("ignored", "1"),
            Property("ignored", "2"),
            Property(
                "party",
                "{\"ignored\":1,\"ignored\":2,\"_gold\":61}"),
            Property(
                "variables",
                $"{{\"ignored\":1,\"ignored\":2,\"_data\":{DataArray("61")}}}"));

        AtlasGoldReadModelResult result = ReadJson(json);

        AssertCandidate(result.PartyGold, AtlasGoldCandidateState.Present, 61);
        AssertCandidate(result.VariableGold, AtlasGoldCandidateState.Present, 61);
        Assert.Equal(AtlasGoldAggregateState.Consistent, result.Aggregate);
        AssertInvariants(result);
    }

    [Fact]
    public void MemberNamesUseExactOrdinalComparison()
    {
        string json = Root(
            Property("Party", "{\"_gold\":99}"),
            Property(
                "party",
                "{\"_Gold\":99,\"_gold\":67}"),
            Property("Variables", $"{{\"_data\":{DataArray("99")}}}"),
            Property(
                "variables",
                $"{{\"_Data\":{DataArray("99")},\"_data\":{DataArray("67")}}}"));

        AtlasGoldReadModelResult result = ReadJson(json);

        AssertCandidate(result.PartyGold, AtlasGoldCandidateState.Present, 67);
        AssertCandidate(result.VariableGold, AtlasGoldCandidateState.Present, 67);
        Assert.Equal(AtlasGoldAggregateState.Consistent, result.Aggregate);
        AssertInvariants(result);
    }

    [Fact]
    public void CancellationIsObservedBeforeWork()
    {
        AtlasSaveReadResult source = ReadSource(Root(Party("1"), Variables("1")));
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(
            () => AtlasGoldReadModel.Read(source, cancellation.Token));
    }

    [Fact]
    public void CancellationIsObservedDuringALargeBoundedMemberScan()
    {
        const int unrelatedMemberCount = 950_000;
        StringBuilder party = new(unrelatedMemberCount * 10 + 32);
        party.Append('{');
        for (int index = 0; index < unrelatedMemberCount; index++)
        {
            if (index > 0)
            {
                party.Append(',');
            }

            party.Append("\"partx\":0");
        }

        party.Append(",\"_gold\":1}");
        AtlasSaveReadResult source = ReadSource(
            Root(Property("party", party.ToString()), Variables("1")));
        using CancellationTokenSource cancellation = new();
        using ManualResetEventSlim cancellationReady = new();
        using ManualResetEventSlim beginScan = new();
        Thread cancellationThread = new(() =>
        {
            cancellationReady.Set();
            beginScan.Wait();
            long cancelAt = Stopwatch.GetTimestamp() + (Stopwatch.Frequency / 1_000);
            while (Stopwatch.GetTimestamp() < cancelAt)
            {
                Thread.SpinWait(64);
            }

            cancellation.Cancel();
        });

        cancellationThread.Start();
        cancellationReady.Wait(TestContext.Current.CancellationToken);
        Assert.False(cancellation.IsCancellationRequested);
        beginScan.Set();
        try
        {
            Assert.ThrowsAny<OperationCanceledException>(
                () => AtlasGoldReadModel.Read(source, cancellation.Token));
        }
        finally
        {
            cancellationThread.Join();
        }
    }

    [Fact]
    public void ReadPreservesGraphJsonCompressedAndSemanticNoOpObservations()
    {
        string json = Root(
            Property("party", "{\"@r\":1}"),
            Variables("73"),
            Property("partyTarget", "{\"@c\":1,\"_gold\":73}"));
        byte[] compressed = AtlasLzStringCodec.CompressToBase64(
            json,
            cancellationToken: TestContext.Current.CancellationToken);
        AtlasSaveReadResult source = AtlasSaveReader.Read(
            compressed,
            cancellationToken: TestContext.Current.CancellationToken);
        AtlasJsonExNode graph = source.Graph;
        AtlasJsonExObject root = Assert.IsType<AtlasJsonExObject>(graph);
        string[] memberNames = root.Members.Select(static member => member.Name).ToArray();
        AtlasJsonExNode[] memberValues =
            root.Members.Select(static member => member.Value).ToArray();
        byte[] jsonBytes = source.Json.Utf8Source.ToArray();
        byte[] originalBytes = source.OriginalCompressedBytes.ToArray();
        byte[] semanticNoOp = source.GetSemanticNoOpBytes();
        AtlasTokenCensus tokenCensus = source.TokenCensus;
        AtlasGraphCensus graphCensus = source.GraphCensus;
        byte[] graphObservations = AtlasStructuralScanner
            .Scan(
                source,
                AtlasDocumentRole.SlotSave,
                cancellationToken: TestContext.Current.CancellationToken)
            .GetCanonicalUtf8Bytes(TestContext.Current.CancellationToken);

        AtlasGoldReadModelResult result = AtlasGoldReadModel.Read(
            source,
            TestContext.Current.CancellationToken);

        AssertCandidate(result.PartyGold, AtlasGoldCandidateState.Present, 73);
        AssertCandidate(result.VariableGold, AtlasGoldCandidateState.Present, 73);
        Assert.Same(graph, source.Graph);
        Assert.Equal(memberNames, root.Members.Select(static member => member.Name));
        Assert.Equal(memberValues.Length, root.Members.Count);
        for (int index = 0; index < memberValues.Length; index++)
        {
            Assert.Same(memberValues[index], root.Members[index].Value);
        }

        Assert.Equal(jsonBytes, source.Json.Utf8Source.ToArray());
        Assert.Equal(originalBytes, source.OriginalCompressedBytes.ToArray());
        Assert.Equal(semanticNoOp, source.GetSemanticNoOpBytes());
        Assert.Equal(compressed, source.GetSemanticNoOpBytes());
        Assert.Equal(tokenCensus, source.TokenCensus);
        Assert.Equal(graphCensus, source.GraphCensus);
        Assert.Equal(
            graphObservations,
            AtlasStructuralScanner
                .Scan(
                    source,
                    AtlasDocumentRole.SlotSave,
                    cancellationToken: TestContext.Current.CancellationToken)
                .GetCanonicalUtf8Bytes(TestContext.Current.CancellationToken));
    }

    [Fact]
    public void PublicShapePreventsContradictoryConstructionOrDiagnosticPayloads()
    {
        Assert.Equal(
            [
                nameof(AtlasGoldCandidateState.Present),
                nameof(AtlasGoldCandidateState.Missing),
                nameof(AtlasGoldCandidateState.Ambiguous),
                nameof(AtlasGoldCandidateState.WrongShape),
                nameof(AtlasGoldCandidateState.NonInteger),
                nameof(AtlasGoldCandidateState.OutsideInt64),
            ],
            Enum.GetNames<AtlasGoldCandidateState>());
        Assert.Equal(
            [
                nameof(AtlasGoldAggregateState.Consistent),
                nameof(AtlasGoldAggregateState.Disagree),
                nameof(AtlasGoldAggregateState.Incomplete),
            ],
            Enum.GetNames<AtlasGoldAggregateState>());
        AssertClosedResultType(
            typeof(AtlasGoldCandidateResult),
            new Dictionary<string, Type>(StringComparer.Ordinal)
            {
                [nameof(AtlasGoldCandidateResult.State)] =
                    typeof(AtlasGoldCandidateState),
                [nameof(AtlasGoldCandidateResult.Value)] = typeof(long?),
            });
        AssertClosedResultType(
            typeof(AtlasGoldReadModelResult),
            new Dictionary<string, Type>(StringComparer.Ordinal)
            {
                [nameof(AtlasGoldReadModelResult.PartyGold)] =
                    typeof(AtlasGoldCandidateResult),
                [nameof(AtlasGoldReadModelResult.VariableGold)] =
                    typeof(AtlasGoldCandidateResult),
                [nameof(AtlasGoldReadModelResult.Aggregate)] =
                    typeof(AtlasGoldAggregateState),
            });

        MethodInfo read = Assert.Single(
            typeof(AtlasGoldReadModel).GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        Assert.Equal(nameof(AtlasGoldReadModel.Read), read.Name);
        Assert.Equal(typeof(AtlasGoldReadModelResult), read.ReturnType);
        ParameterInfo[] parameters = read.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(AtlasSaveReadResult), parameters[0].ParameterType);
        Assert.Equal(typeof(CancellationToken), parameters[1].ParameterType);
        Assert.True(parameters[1].HasDefaultValue);
        Assert.Null(parameters[1].DefaultValue);
    }

    [Fact]
    public void ArgumentFailureUsesFixedValueFreeTextAndStatesDoNotThrow()
    {
        const string candidateValue = "922337203685477580812345";
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => AtlasGoldReadModel.Read(
                null!,
                TestContext.Current.CancellationToken));

        Assert.Equal("source", exception.ParamName);
        Assert.Contains(
            "The Atlas save read result is required.",
            exception.Message,
            StringComparison.Ordinal);
        Assert.DoesNotContain(candidateValue, exception.Message, StringComparison.Ordinal);

        AtlasGoldReadModelResult result = ReadJson(
            Root(Party(candidateValue), Variables("\"synthetic-value\"")));
        AssertCandidate(
            result.PartyGold,
            AtlasGoldCandidateState.OutsideInt64,
            expectedValue: null);
        AssertCandidate(
            result.VariableGold,
            AtlasGoldCandidateState.WrongShape,
            expectedValue: null);
        Assert.Equal(AtlasGoldAggregateState.Incomplete, result.Aggregate);
    }

    private static void AssertClosedResultType(
        Type type,
        Dictionary<string, Type> expectedProperties)
    {
        Assert.True(type.IsSealed);
        Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        Assert.Empty(
            type.GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly));
        PropertyInfo[] properties = type.GetProperties(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Equal(expectedProperties.Count, properties.Length);
        foreach (PropertyInfo property in properties)
        {
            Assert.True(expectedProperties.TryGetValue(property.Name, out Type? expectedType));
            Assert.Equal(expectedType, property.PropertyType);
            Assert.Null(property.SetMethod);
        }

        FieldInfo[] fields = type.GetFields(
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Equal(expectedProperties.Count, fields.Length);
        Assert.All(fields, static field => Assert.True(field.IsInitOnly));
        Assert.Equal(
            expectedProperties.Values.OrderBy(
                static value => value.FullName,
                StringComparer.Ordinal),
            fields.Select(static field => field.FieldType)
                .OrderBy(static value => value.FullName, StringComparer.Ordinal));
    }

    private static void AssertInvariants(AtlasGoldReadModelResult result)
    {
        Assert.Equal(
            result.PartyGold.State == AtlasGoldCandidateState.Present,
            result.PartyGold.Value.HasValue);
        Assert.Equal(
            result.VariableGold.State == AtlasGoldCandidateState.Present,
            result.VariableGold.Value.HasValue);

        AtlasGoldAggregateState expected =
            result.PartyGold.Value is long party
            && result.VariableGold.Value is long variable
                ? party == variable
                    ? AtlasGoldAggregateState.Consistent
                    : AtlasGoldAggregateState.Disagree
                : AtlasGoldAggregateState.Incomplete;
        Assert.Equal(expected, result.Aggregate);
    }

    private static void AssertCandidate(
        AtlasGoldCandidateResult candidate,
        AtlasGoldCandidateState expectedState,
        long? expectedValue)
    {
        Assert.Equal(expectedState, candidate.State);
        Assert.Equal(expectedValue, candidate.Value);
    }

    private static long? ExpectedValue(AtlasGoldCandidateState state) =>
        state == AtlasGoldCandidateState.Present ? 7 : null;

    private static AtlasGoldReadModelResult ReadJson(string json) =>
        AtlasGoldReadModel.Read(
            ReadSource(json),
            TestContext.Current.CancellationToken);

    private static AtlasSaveReadResult ReadSource(string json) =>
        AtlasSaveReader.Read(
            AtlasLzStringCodec.CompressToBase64(
                json,
                cancellationToken: TestContext.Current.CancellationToken),
            cancellationToken: TestContext.Current.CancellationToken);

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
        StringBuilder builder = new(elementCount * (filler.Length + 1) + gold.Length + 2);
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
