using System.Reflection;
using Hcoona.CelesphoniaModifier.Atlas;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class AtlasStructuralScannerTests
{
    private const string ComprehensiveJson =
        "{\"first\":1,\"duplicate\":\"synthetic-private-😀\","
        + "\"duplicate\":{\"@c\":701,\"@\":\"Synthetic.Private.Type\","
        + "\"values\":{\"@c\":702,\"@a\":[true,{\"@r\":701}]},"
        + "\"self\":{\"@r\":701}},\"again\":{\"@r\":702},\"tail\":null}";

    [Fact]
    public void ScanProducesExactDetachedPreorderAndCensus()
    {
        AtlasStructuralScanResult scan = Scan(ComprehensiveJson);
        AtlasStructuralScanDocument document = scan.Document;

        Assert.Equal(AtlasDocumentRole.GlobalSave, document.DocumentRole);
        Assert.Equal(
            new AtlasStructuralScanCensus(
                NodeOccurrences: 10,
                ObjectOccurrences: 2,
                ArrayOccurrences: 1,
                ScalarOccurrences: 4,
                ReferenceOccurrences: 3,
                OrdinaryMemberEdges: 7,
                ArrayElementEdges: 2,
                IdentityDefinitions: 2,
                ClassMarkers: 1,
                IdentityArrayWrappers: 1,
                DistinctReferencedDefinitions: 2
            ),
            document.Census
        );

        AssertObservation<AtlasStructuralObjectObservation>(
            document,
            0,
            AtlasStructuralLocatorSubject.NodeOccurrence
        );
        AssertScalar(document, 1, AtlasStructuralScalarKind.Number, Member(0));
        AssertScalar(document, 2, AtlasStructuralScalarKind.Text, Member(1));
        AtlasStructuralObjectObservation identityObject =
            AssertObservation<AtlasStructuralObjectObservation>(
                document,
                3,
                AtlasStructuralLocatorSubject.NodeOccurrence,
                Member(2)
            );
        Assert.Equal(AtlasStructuralObjectShape.IdentityObject, identityObject.Shape);
        Assert.Equal(2, identityObject.ChildCount);
        Assert.True(identityObject.ClassMarkerPresent);
        AssertLocator(
            Assert.IsType<AtlasStructuralLocator>(identityObject.IdentityDefinitionLocator),
            AtlasStructuralLocatorSubject.IdentityDefinition,
            Member(2)
        );

        AtlasStructuralArrayObservation identityArray =
            AssertObservation<AtlasStructuralArrayObservation>(
                document,
                4,
                AtlasStructuralLocatorSubject.NodeOccurrence,
                Member(2),
                Member(0)
            );
        Assert.Equal(AtlasStructuralArrayShape.IdentityArrayWrapper, identityArray.Shape);
        Assert.Equal(2, identityArray.ChildCount);
        AssertLocator(
            Assert.IsType<AtlasStructuralLocator>(identityArray.IdentityDefinitionLocator),
            AtlasStructuralLocatorSubject.IdentityDefinition,
            Member(2),
            Member(0)
        );
        AssertScalar(document, 5, AtlasStructuralScalarKind.True, Member(2), Member(0), Element(0));
        AssertReference(document, 6, [Member(2), Member(0), Element(1)], [Member(2)]);
        AssertReference(document, 7, [Member(2), Member(1)], [Member(2)]);
        AssertReference(document, 8, [Member(3)], [Member(2), Member(0)]);
        AssertScalar(document, 9, AtlasStructuralScalarKind.Null, Member(4));

        Assert.DoesNotContain(
            GetReachableObjects(document),
            value =>
                value
                    is AtlasSaveReadResult
                        or AtlasJsonExNode
                        or AtlasLosslessJsonValue
                        or AtlasLosslessJsonDocument
                        or ReadOnlyMemory<byte>
                        or byte[]
        );
    }

    [Fact]
    public void ScannerMatchesIndependentLosslessSyntaxOracle()
    {
        AtlasSaveReadResult source = ReadJson(ComprehensiveJson);
        OracleResult expected = BuildOracle(source.Json.Root);
        AtlasStructuralScanDocument actual = AtlasStructuralScanner
            .Scan(
                source,
                AtlasDocumentRole.GlobalSave,
                cancellationToken: TestContext.Current.CancellationToken
            )
            .Document;

        Assert.Equal(expected.Census, actual.Census);
        Assert.Equal(expected.Observations, actual.Observations.Select(ToOracleObservation));
    }

    [Fact]
    public void ScanIsDeterministicAndRedactsNamesValuesClassesAndIdentities()
    {
        byte[] first = Scan(ComprehensiveJson)
            .GetCanonicalUtf8Bytes(TestContext.Current.CancellationToken);
        byte[] second = Scan(ComprehensiveJson)
            .GetCanonicalUtf8Bytes(TestContext.Current.CancellationToken);
        string json = System.Text.Encoding.UTF8.GetString(first);

        Assert.Equal(first, second);
        Assert.EndsWith("\n", json, StringComparison.Ordinal);
        Assert.DoesNotContain("first", json, StringComparison.Ordinal);
        Assert.DoesNotContain("duplicate", json, StringComparison.Ordinal);
        Assert.DoesNotContain("synthetic-private", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Synthetic.Private.Type", json, StringComparison.Ordinal);
        Assert.DoesNotContain("701", json, StringComparison.Ordinal);
        Assert.DoesNotContain("702", json, StringComparison.Ordinal);
        Assert.DoesNotContain("@c", json, StringComparison.Ordinal);
        Assert.DoesNotContain("@a", json, StringComparison.Ordinal);
        Assert.DoesNotContain("@r", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferencesRemainLeavesAcrossSelfCyclesAndSharedTargets()
    {
        AtlasStructuralScanDocument self = Scan(
            "{\"@c\":1,\"self\":{\"@r\":1},\"again\":{\"@r\":1}}"
        ).Document;
        Assert.Equal(3, self.Observations.Count);
        Assert.Equal(2, self.Census.ReferenceOccurrences);
        Assert.Equal(1, self.Census.DistinctReferencedDefinitions);

        AtlasStructuralScanDocument shared = Scan(
            "{\"forward\":{\"@r\":2},\"target\":{\"@c\":2,\"value\":1}," + "\"back\":{\"@r\":2}}"
        ).Document;
        Assert.Equal(5, shared.Observations.Count);
        Assert.Equal(2, shared.Census.ReferenceOccurrences);
        Assert.Equal(1, shared.Census.DistinctReferencedDefinitions);

        AtlasStructuralScanDocument longerCycle = Scan(
            "[{\"@c\":1,\"next\":{\"@r\":2}}," + "{\"@c\":2,\"next\":{\"@r\":1}}]"
        ).Document;
        Assert.Equal(5, longerCycle.Observations.Count);
        Assert.Equal(2, longerCycle.Census.ReferenceOccurrences);
        Assert.Equal(2, longerCycle.Census.DistinctReferencedDefinitions);
    }

    [Fact]
    public void EmptyAndSparseContainersRetainExactShapesAndCounts()
    {
        AtlasStructuralScanDocument document = Scan(
            "{\"emptyObject\":{},\"emptyArray\":[],\"nested\":[{},[]]}"
        ).Document;

        Assert.Equal(6, document.Observations.Count);
        Assert.Equal(3, document.Census.ObjectOccurrences);
        Assert.Equal(3, document.Census.ArrayOccurrences);
        Assert.All(
            document.Observations.Skip(1),
            observation =>
            {
                if (observation is AtlasStructuralObjectObservation objectObservation)
                {
                    Assert.Equal(AtlasStructuralObjectShape.PlainObject, objectObservation.Shape);
                }

                if (observation is AtlasStructuralArrayObservation arrayObservation)
                {
                    Assert.Equal(AtlasStructuralArrayShape.PlainArray, arrayObservation.Shape);
                }
            }
        );
    }

    [Fact]
    public void SupplementaryPlaneTextIsAcceptedAndUnpairedSurrogatesRemainRejected()
    {
        AtlasStructuralScanDocument document = Scan("{\"value\":\"snow-😀-star\"}").Document;
        Assert.Equal(
            AtlasStructuralScalarKind.Text,
            Assert.IsType<AtlasStructuralScalarObservation>(document.Observations[1]).ScalarKind
        );

        AtlasSaveReadException exception = Assert.Throws<AtlasSaveReadException>(() =>
            ReadJson("{\"value\":\"\\uD800\"}")
        );
        Assert.Equal(AtlasSaveReadFailure.MalformedJson, exception.Failure);
    }

    [Fact]
    public void ScannerClassifiesContainmentAliasesAndCycles()
    {
        AtlasSaveReadResult aliasSource = ReadJson("{\"left\":0}");
        AtlasJsonExObject aliasRoot = Assert.IsType<AtlasJsonExObject>(aliasSource.Graph);
        List<AtlasJsonExMember> aliasMembers = GetMutableMembers(aliasRoot);
        aliasMembers.Add(
            new AtlasJsonExMember("synthetic", "\"synthetic\"", aliasMembers[0].Value)
        );
        AtlasStructuralScanException alias = Assert.Throws<AtlasStructuralScanException>(() =>
            AtlasStructuralScanner.Scan(
                aliasSource,
                AtlasDocumentRole.GlobalSave,
                cancellationToken: TestContext.Current.CancellationToken
            )
        );
        Assert.Equal(AtlasStructuralScanFailure.ContainmentAlias, alias.Failure);

        AtlasSaveReadResult cycleSource = ReadJson("{\"left\":0}");
        AtlasJsonExObject cycleRoot = Assert.IsType<AtlasJsonExObject>(cycleSource.Graph);
        GetMutableMembers(cycleRoot)
            .Insert(0, new AtlasJsonExMember("synthetic", "\"synthetic\"", cycleRoot));
        AtlasStructuralScanException cycle = Assert.Throws<AtlasStructuralScanException>(() =>
            AtlasStructuralScanner.Scan(
                cycleSource,
                AtlasDocumentRole.GlobalSave,
                cancellationToken: TestContext.Current.CancellationToken
            )
        );
        Assert.Equal(AtlasStructuralScanFailure.ContainmentCycle, cycle.Failure);

        AtlasSaveReadResult missingTargetSource = ReadJson("[{\"@c\":1},{\"@r\":1}]");
        AtlasJsonExReference missingTarget = Assert.IsType<AtlasJsonExReference>(
            Assert.IsType<AtlasJsonExArray>(missingTargetSource.Graph).Elements[1]
        );
        typeof(AtlasJsonExReference)
            .GetProperty(nameof(AtlasJsonExReference.Target))!
            .SetValue(missingTarget, null);
        AtlasStructuralScanException missing = Assert.Throws<AtlasStructuralScanException>(() =>
            AtlasStructuralScanner.Scan(
                missingTargetSource,
                AtlasDocumentRole.GlobalSave,
                cancellationToken: TestContext.Current.CancellationToken
            )
        );
        Assert.Equal(AtlasStructuralScanFailure.MissingReferenceTarget, missing.Failure);
    }

    [Fact]
    public void ScannerLimitsHonorBelowEqualAndAboveBoundaries()
    {
        AtlasSaveReadResult source = ReadJson("[0]");
        AtlasStructuralScanResult baseline = AtlasStructuralScanner.Scan(
            source,
            AtlasDocumentRole.SlotSave,
            cancellationToken: TestContext.Current.CancellationToken
        );
        int canonicalUtf8Length = baseline
            .GetCanonicalUtf8Bytes(TestContext.Current.CancellationToken)
            .Length;

        _ = AtlasStructuralScanner.Scan(
            source,
            AtlasDocumentRole.SlotSave,
            new AtlasStructuralScannerLimits
            {
                MaximumObservations = 2,
                MaximumLocatorDepth = 1,
                MaximumRetainedLocatorSegments = 1,
                MaximumCanonicalUtf8Bytes = canonicalUtf8Length,
            },
            TestContext.Current.CancellationToken
        );
        _ = AtlasStructuralScanner.Scan(
            source,
            AtlasDocumentRole.SlotSave,
            new AtlasStructuralScannerLimits
            {
                MaximumObservations = 3,
                MaximumLocatorDepth = 2,
                MaximumRetainedLocatorSegments = 2,
                MaximumCanonicalUtf8Bytes = canonicalUtf8Length + 1,
            },
            TestContext.Current.CancellationToken
        );

        AssertLimit(
            AtlasStructuralScanFailure.ObservationLimit,
            new AtlasStructuralScannerLimits { MaximumObservations = 1 }
        );
        AssertLimit(
            AtlasStructuralScanFailure.LocatorDepthLimit,
            new AtlasStructuralScannerLimits { MaximumLocatorDepth = 0 }
        );
        AssertLimit(
            AtlasStructuralScanFailure.RetainedSegmentLimit,
            new AtlasStructuralScannerLimits { MaximumRetainedLocatorSegments = 0 }
        );
        AssertLimit(
            AtlasStructuralScanFailure.CanonicalSerializationLimit,
            new AtlasStructuralScannerLimits
            {
                MaximumCanonicalUtf8Bytes = canonicalUtf8Length - 1,
            }
        );

        void AssertLimit(AtlasStructuralScanFailure expected, AtlasStructuralScannerLimits limits)
        {
            AtlasStructuralScanException exception = Assert.Throws<AtlasStructuralScanException>(
                () =>
                    AtlasStructuralScanner.Scan(
                        source,
                        AtlasDocumentRole.SlotSave,
                        limits,
                        TestContext.Current.CancellationToken
                    )
            );
            Assert.Equal(expected, exception.Failure);
        }
    }

    [Fact]
    public async Task ScannerObservesCancellationBeforeAndDuringTraversal()
    {
        using CancellationTokenSource before = new();
        await before.CancelAsync();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            AtlasStructuralScanner.Scan(
                ReadJson("[0]"),
                AtlasDocumentRole.ConfigSave,
                cancellationToken: before.Token
            )
        );

        string json = "[" + string.Join(",", Enumerable.Repeat("0", 100_000)) + "]";
        AtlasSaveReadResult source = ReadJson(json);
        using CancellationTokenSource during = new();
        Task<AtlasStructuralScanResult> task = Task.Run(() =>
            AtlasStructuralScanner.Scan(
                source,
                AtlasDocumentRole.ConfigSave,
                cancellationToken: during.Token
            )
        );
        await during.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await task);
    }

    [Fact]
    public async Task ReferenceResolutionPassObservesCancellation()
    {
        const int referenceCount = 160_000;
        const int pacingReferenceCount = 120_000;
        AtlasSaveReadResult source = ReadJson(CreateReferenceDominatedJson(referenceCount));
        AtlasSaveReadResult pacingSource = ReadJson(
            CreateReferenceDominatedJson(pacingReferenceCount)
        );
        AtlasStructuralScannerLimits limits = AtlasStructuralScannerLimits.Default;
        _ = AtlasStructuralScanner.BuildDocument(
            ReadJson(CreateReferenceDominatedJson(10)),
            AtlasDocumentRole.ConfigSave,
            limits,
            TestContext.Current.CancellationToken
        );

        using CancellationTokenSource during = new();
        using ManualResetEventSlim started = new();
        Task<AtlasStructuralScanDocument> scanning = Task.Factory.StartNew(
            () =>
            {
                started.Set();
                return AtlasStructuralScanner.BuildDocument(
                    source,
                    AtlasDocumentRole.ConfigSave,
                    limits,
                    during.Token
                );
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default
        );

        Assert.True(
            started.Wait(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken
            )
        );
        // Pace cancellation with same-shape work so it lands late without a production phase hook.
        _ = AtlasStructuralScanner.BuildDocument(
            pacingSource,
            AtlasDocumentRole.ConfigSave,
            limits,
            TestContext.Current.CancellationToken
        );
        Assert.False(scanning.IsCompleted);
        await during.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await scanning);
    }

    [Fact]
    public void PublicA4SurfaceIsInMemoryAndHasNoFilesystemOrCliTypes()
    {
        Type[] types =
        [
            typeof(AtlasStructuralScanner),
            typeof(AtlasStructuralScanJson),
            typeof(AtlasStructuralScanDocument),
            typeof(AtlasStructuralScanResult),
        ];
        Type[] forbidden = [typeof(Stream), typeof(FileInfo), typeof(DirectoryInfo)];

        foreach (
            MethodInfo method in types.SelectMany(static type =>
                type.GetMethods(
                    BindingFlags.Public
                        | BindingFlags.Static
                        | BindingFlags.Instance
                        | BindingFlags.DeclaredOnly
                )
            )
        )
        {
            Assert.DoesNotContain(method.ReturnType, forbidden);
            Assert.DoesNotContain(
                method.GetParameters(),
                parameter => forbidden.Contains(parameter.ParameterType)
            );
        }
    }

    private static IEnumerable<object> GetReachableObjects(object root)
    {
        HashSet<object> visited = new(ReferenceEqualityComparer.Instance);
        Stack<object> pending = [];
        pending.Push(root);
        while (pending.Count > 0)
        {
            object current = pending.Pop();
            if (!visited.Add(current))
            {
                continue;
            }

            yield return current;
            if (current is string or Type || current.GetType().IsValueType)
            {
                continue;
            }

            if (current is System.Collections.IEnumerable sequence)
            {
                foreach (object? item in sequence)
                {
                    if (item is not null)
                    {
                        pending.Push(item);
                    }
                }
            }

            foreach (
                FieldInfo field in current
                    .GetType()
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            )
            {
                object? value = field.GetValue(current);
                if (value is not null)
                {
                    pending.Push(value);
                }
            }
        }
    }

    private static OracleResult BuildOracle(AtlasLosslessJsonValue root)
    {
        Dictionary<int, string> identities = [];
        CollectOracleIdentities(root, string.Empty, identities);
        List<OracleObservation> observations = [];
        BuildOracleObservations(root, string.Empty, identities, observations);

        long objects = observations.Count(static observation => observation.Kind == "object");
        long arrays = observations.Count(static observation => observation.Kind == "array");
        long scalars = observations.Count(static observation => observation.Kind == "scalar");
        long references = observations.Count(static observation => observation.Kind == "reference");
        long ordinaryEdges = observations.Count(static observation =>
            GetLastSegment(observation.Path).StartsWith('m')
        );
        long arrayEdges = observations.Count(static observation =>
            GetLastSegment(observation.Path).StartsWith('a')
        );
        long identityDefinitions = observations.Count(static observation =>
            observation.IdentityPresent
        );
        long classMarkers = observations.Count(static observation =>
            observation.ClassMarkerPresent
        );
        long identityArrays = observations.Count(static observation =>
            observation.IdentityArrayWrapper
        );
        long distinctTargets = observations
            .Where(static observation => observation.TargetPath is not null)
            .Select(static observation => observation.TargetPath)
            .Distinct(StringComparer.Ordinal)
            .LongCount();
        AtlasStructuralScanCensus census = new(
            observations.Count,
            objects,
            arrays,
            scalars,
            references,
            ordinaryEdges,
            arrayEdges,
            identityDefinitions,
            classMarkers,
            identityArrays,
            distinctTargets
        );
        return new OracleResult(observations.AsReadOnly(), census);
    }

    private static void CollectOracleIdentities(
        AtlasLosslessJsonValue value,
        string path,
        Dictionary<int, string> identities
    )
    {
        switch (value)
        {
            case AtlasLosslessJsonScalar:
                return;
            case AtlasLosslessJsonArray array:
                for (int index = 0; index < array.Elements.Count; index++)
                {
                    CollectOracleIdentities(
                        array.Elements[index],
                        AppendOraclePath(path, $"a{index}"),
                        identities
                    );
                }

                return;
            case AtlasLosslessJsonObject objectValue:
                AtlasLosslessJsonMember? reference = FindMarker(objectValue, "@r");
                if (reference is not null)
                {
                    return;
                }

                AtlasLosslessJsonMember? identity = FindMarker(objectValue, "@c");
                if (identity is not null)
                {
                    identities.Add(ReadOracleIdentity(identity.Value), path);
                }

                AtlasLosslessJsonMember? arrayMarker = FindMarker(objectValue, "@a");
                if (arrayMarker?.Value is AtlasLosslessJsonArray wrapped)
                {
                    for (int index = 0; index < wrapped.Elements.Count; index++)
                    {
                        CollectOracleIdentities(
                            wrapped.Elements[index],
                            AppendOraclePath(path, $"a{index}"),
                            identities
                        );
                    }

                    return;
                }

                int ordinal = 0;
                foreach (AtlasLosslessJsonMember member in objectValue.Members)
                {
                    if (IsMarker(member.Name))
                    {
                        continue;
                    }

                    CollectOracleIdentities(
                        member.Value,
                        AppendOraclePath(path, $"m{ordinal}"),
                        identities
                    );
                    ordinal++;
                }

                return;
            default:
                throw new InvalidOperationException();
        }
    }

    private static void BuildOracleObservations(
        AtlasLosslessJsonValue value,
        string path,
        IReadOnlyDictionary<int, string> identities,
        List<OracleObservation> observations
    )
    {
        switch (value)
        {
            case AtlasLosslessJsonScalar scalar:
                observations.Add(
                    new OracleObservation(
                        "scalar",
                        path,
                        null,
                        MapOracleScalarKind(scalar.Kind),
                        0,
                        ClassMarkerPresent: false,
                        IdentityPresent: false,
                        IdentityArrayWrapper: false
                    )
                );
                return;
            case AtlasLosslessJsonArray array:
                observations.Add(
                    new OracleObservation(
                        "array",
                        path,
                        null,
                        null,
                        array.Elements.Count,
                        ClassMarkerPresent: false,
                        IdentityPresent: false,
                        IdentityArrayWrapper: false
                    )
                );
                for (int index = 0; index < array.Elements.Count; index++)
                {
                    BuildOracleObservations(
                        array.Elements[index],
                        AppendOraclePath(path, $"a{index}"),
                        identities,
                        observations
                    );
                }

                return;
            case AtlasLosslessJsonObject objectValue:
                AtlasLosslessJsonMember? reference = FindMarker(objectValue, "@r");
                if (reference is not null)
                {
                    observations.Add(
                        new OracleObservation(
                            "reference",
                            path,
                            identities[ReadOracleIdentity(reference.Value)],
                            null,
                            0,
                            ClassMarkerPresent: false,
                            IdentityPresent: false,
                            IdentityArrayWrapper: false
                        )
                    );
                    return;
                }

                AtlasLosslessJsonMember? identity = FindMarker(objectValue, "@c");
                AtlasLosslessJsonMember? arrayMarker = FindMarker(objectValue, "@a");
                if (arrayMarker?.Value is AtlasLosslessJsonArray wrapped)
                {
                    observations.Add(
                        new OracleObservation(
                            "array",
                            path,
                            null,
                            null,
                            wrapped.Elements.Count,
                            ClassMarkerPresent: false,
                            IdentityPresent: true,
                            IdentityArrayWrapper: true
                        )
                    );
                    for (int index = 0; index < wrapped.Elements.Count; index++)
                    {
                        BuildOracleObservations(
                            wrapped.Elements[index],
                            AppendOraclePath(path, $"a{index}"),
                            identities,
                            observations
                        );
                    }

                    return;
                }

                AtlasLosslessJsonMember[] ordinary =
                [
                    .. objectValue.Members.Where(static member => !IsMarker(member.Name)),
                ];
                observations.Add(
                    new OracleObservation(
                        "object",
                        path,
                        null,
                        null,
                        ordinary.Length,
                        FindMarker(objectValue, "@") is not null,
                        identity is not null,
                        IdentityArrayWrapper: false
                    )
                );
                for (int ordinal = 0; ordinal < ordinary.Length; ordinal++)
                {
                    BuildOracleObservations(
                        ordinary[ordinal].Value,
                        AppendOraclePath(path, $"m{ordinal}"),
                        identities,
                        observations
                    );
                }

                return;
            default:
                throw new InvalidOperationException();
        }
    }

    private static OracleObservation ToOracleObservation(AtlasStructuralObservation observation) =>
        observation switch
        {
            AtlasStructuralScalarObservation scalar => new OracleObservation(
                "scalar",
                FormatOraclePath(scalar.Locator),
                null,
                scalar.ScalarKind,
                0,
                ClassMarkerPresent: false,
                IdentityPresent: false,
                IdentityArrayWrapper: false
            ),
            AtlasStructuralObjectObservation objectObservation => new OracleObservation(
                "object",
                FormatOraclePath(objectObservation.Locator),
                null,
                null,
                objectObservation.ChildCount,
                objectObservation.ClassMarkerPresent,
                objectObservation.IdentityDefinitionPresent,
                IdentityArrayWrapper: false
            ),
            AtlasStructuralArrayObservation arrayObservation => new OracleObservation(
                "array",
                FormatOraclePath(arrayObservation.Locator),
                null,
                null,
                arrayObservation.ChildCount,
                ClassMarkerPresent: false,
                arrayObservation.IdentityDefinitionPresent,
                arrayObservation.Shape == AtlasStructuralArrayShape.IdentityArrayWrapper
            ),
            AtlasStructuralReferenceObservation reference => new OracleObservation(
                "reference",
                FormatOraclePath(reference.Locator),
                FormatOraclePath(reference.TargetIdentityDefinitionLocator),
                null,
                0,
                ClassMarkerPresent: false,
                IdentityPresent: false,
                IdentityArrayWrapper: false
            ),
            _ => throw new InvalidOperationException(),
        };

    private static string FormatOraclePath(AtlasStructuralLocator locator) =>
        string.Join(
            "/",
            locator.Segments.Select(static segment =>
                segment switch
                {
                    AtlasOrdinaryMemberLocatorSegment ordinary => $"m{ordinary.Ordinal}",
                    AtlasArrayElementLocatorSegment array => $"a{array.Index}",
                    _ => throw new InvalidOperationException(),
                }
            )
        );

    private static string AppendOraclePath(string path, string segment) =>
        path.Length == 0 ? segment : $"{path}/{segment}";

    private static string GetLastSegment(string path)
    {
        int separator = path.LastIndexOf('/');
        return separator < 0 ? path : path[(separator + 1)..];
    }

    private static AtlasLosslessJsonMember? FindMarker(
        AtlasLosslessJsonObject value,
        string name
    ) => value.Members.SingleOrDefault(member => StringComparer.Ordinal.Equals(member.Name, name));

    private static bool IsMarker(string name) => name is "@" or "@c" or "@a" or "@r";

    private static int ReadOracleIdentity(AtlasLosslessJsonValue value) =>
        int.Parse(
            Assert.IsType<AtlasLosslessJsonScalar>(value).RawLexeme,
            System.Globalization.CultureInfo.InvariantCulture
        );

    private static AtlasStructuralScalarKind MapOracleScalarKind(AtlasJsonScalarKind kind) =>
        kind switch
        {
            AtlasJsonScalarKind.Text => AtlasStructuralScalarKind.Text,
            AtlasJsonScalarKind.Number => AtlasStructuralScalarKind.Number,
            AtlasJsonScalarKind.True => AtlasStructuralScalarKind.True,
            AtlasJsonScalarKind.False => AtlasStructuralScalarKind.False,
            AtlasJsonScalarKind.Null => AtlasStructuralScalarKind.Null,
            _ => throw new InvalidOperationException(),
        };

    private static List<AtlasJsonExMember> GetMutableMembers(AtlasJsonExObject node)
    {
        FieldInfo field =
            typeof(AtlasJsonExObject).GetField(
                "members",
                BindingFlags.Instance | BindingFlags.NonPublic
            ) ?? throw new InvalidOperationException();
        return Assert.IsType<List<AtlasJsonExMember>>(field.GetValue(node));
    }

    private static void AssertScalar(
        AtlasStructuralScanDocument document,
        int index,
        AtlasStructuralScalarKind kind,
        params AtlasStructuralLocatorSegment[] segments
    )
    {
        AtlasStructuralScalarObservation scalar =
            AssertObservation<AtlasStructuralScalarObservation>(
                document,
                index,
                AtlasStructuralLocatorSubject.NodeOccurrence,
                segments
            );
        Assert.Equal(kind, scalar.ScalarKind);
    }

    private static void AssertReference(
        AtlasStructuralScanDocument document,
        int index,
        AtlasStructuralLocatorSegment[] occurrence,
        AtlasStructuralLocatorSegment[] target
    )
    {
        AtlasStructuralReferenceObservation reference =
            AssertObservation<AtlasStructuralReferenceObservation>(
                document,
                index,
                AtlasStructuralLocatorSubject.ReferenceOccurrence,
                occurrence
            );
        AssertLocator(
            reference.TargetIdentityDefinitionLocator,
            AtlasStructuralLocatorSubject.IdentityDefinition,
            target
        );
    }

    private static T AssertObservation<T>(
        AtlasStructuralScanDocument document,
        int index,
        AtlasStructuralLocatorSubject subject,
        params AtlasStructuralLocatorSegment[] segments
    )
        where T : AtlasStructuralObservation
    {
        T observation = Assert.IsType<T>(document.Observations[index]);
        AssertLocator(observation.Locator, subject, segments);
        return observation;
    }

    private static void AssertLocator(
        AtlasStructuralLocator locator,
        AtlasStructuralLocatorSubject subject,
        params AtlasStructuralLocatorSegment[] segments
    )
    {
        Assert.Equal(subject, locator.Subject);
        Assert.Equal(segments, locator.Segments);
    }

    private static AtlasOrdinaryMemberLocatorSegment Member(long ordinal) => new(ordinal);

    private static AtlasArrayElementLocatorSegment Element(long index) => new(index);

    private static string CreateReferenceDominatedJson(int referenceCount) =>
        "{\"@c\":1,\"references\":["
        + string.Join(",", Enumerable.Repeat("{\"@r\":1}", referenceCount))
        + "]}";

    private static AtlasStructuralScanResult Scan(string json) =>
        AtlasStructuralScanner.Scan(
            ReadJson(json),
            AtlasDocumentRole.GlobalSave,
            cancellationToken: TestContext.Current.CancellationToken
        );

    private static AtlasSaveReadResult ReadJson(string json) =>
        AtlasSaveReader.Read(
            AtlasLzStringCodec.CompressToBase64(
                json,
                cancellationToken: TestContext.Current.CancellationToken
            ),
            cancellationToken: TestContext.Current.CancellationToken
        );

    private sealed record OracleObservation(
        string Kind,
        string Path,
        string? TargetPath,
        AtlasStructuralScalarKind? ScalarKind,
        long ChildCount,
        bool ClassMarkerPresent,
        bool IdentityPresent,
        bool IdentityArrayWrapper
    );

    private sealed record OracleResult(
        IReadOnlyList<OracleObservation> Observations,
        AtlasStructuralScanCensus Census
    );
}
