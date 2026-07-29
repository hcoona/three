using System.Text;
using System.Text.Json;
using Hcoona.CelesphoniaModifier.Atlas;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class AtlasStructuralScanJsonTests
{
    [Fact]
    public void CanonicalScalarDocumentHasExactBytesAndRoundTrips()
    {
        AtlasSaveReadResult source = ReadJson("0");
        AtlasStructuralScanResult scan = AtlasStructuralScanner.Scan(
            source,
            AtlasDocumentRole.ConfigSave,
            cancellationToken: TestContext.Current.CancellationToken
        );
        const string expected =
            "{\"schemaVersion\":\"atlas-structural-scan/v1\","
            + "\"documentRole\":\"config-save\",\"census\":{"
            + "\"nodeOccurrences\":1,\"objectOccurrences\":0,"
            + "\"arrayOccurrences\":0,\"scalarOccurrences\":1,"
            + "\"referenceOccurrences\":0,\"ordinaryMemberEdges\":0,"
            + "\"arrayElementEdges\":0,\"identityDefinitions\":0,"
            + "\"classMarkers\":0,\"identityArrayWrappers\":0,"
            + "\"distinctReferencedDefinitions\":0},\"observations\":[{"
            + "\"locator\":{\"subject\":\"node-occurrence\",\"segments\":[]},"
            + "\"kind\":\"scalar\",\"scalarKind\":\"number\"}]}\n";

        Assert.Equal(Encoding.UTF8.GetBytes(expected), scan.GetCanonicalUtf8Bytes());
        AtlasStructuralScanResult parsed = AtlasStructuralScanJson.Parse(
            scan.CanonicalUtf8,
            source,
            AtlasDocumentRole.ConfigSave,
            cancellationToken: TestContext.Current.CancellationToken
        );
        Assert.Equal(scan.GetCanonicalUtf8Bytes(), parsed.GetCanonicalUtf8Bytes());
        Assert.NotSame(scan.Document, parsed.Document);
    }

    [Fact]
    public void ParserRejectsMalformedNoncanonicalAndVariantInputs()
    {
        AtlasSaveReadResult source = ReadJson("0");
        byte[] canonical = AtlasStructuralScanner
            .Scan(
                source,
                AtlasDocumentRole.GlobalSave,
                cancellationToken: TestContext.Current.CancellationToken
            )
            .GetCanonicalUtf8Bytes();
        string text = Encoding.UTF8.GetString(canonical);
        List<byte[]> invalid =
        [
            [0xEF, 0xBB, 0xBF, .. canonical],
            canonical[..^1],
            [.. canonical, (byte)'\n'],
            Encoding.UTF8.GetBytes(text[..^1] + " \n"),
            Encoding.UTF8.GetBytes(
                text.Replace(
                    "\"schemaVersion\":\"atlas-structural-scan/v1\",",
                    "\"schemaVersion\":\"atlas-structural-scan/v1\","
                        + "\"schemaVersion\":\"atlas-structural-scan/v1\",",
                    StringComparison.Ordinal
                )
            ),
            Encoding.UTF8.GetBytes(
                text.Replace(
                    "\"documentRole\":\"global-save\"",
                    "\"documentRole\":null",
                    StringComparison.Ordinal
                )
            ),
            Encoding.UTF8.GetBytes(
                text.Replace(
                    "\"nodeOccurrences\":1",
                    "\"nodeOccurrences\":1.0",
                    StringComparison.Ordinal
                )
            ),
            Encoding.UTF8.GetBytes(
                text.Replace(
                    "\"scalarKind\":\"number\"",
                    "\"scalarKind\":\"number\",\"extra\":0",
                    StringComparison.Ordinal
                )
            ),
            Encoding.UTF8.GetBytes(text[..^2] + ",\"unknown\":0}\n"),
            Encoding.UTF8.GetBytes(text[..^1] + "x\n"),
        ];
        byte[] malformedUtf8 = canonical.ToArray();
        int roleOffset = text.IndexOf("global-save", StringComparison.Ordinal);
        malformedUtf8[roleOffset] = 0xFF;
        invalid.Add(malformedUtf8);

        foreach (byte[] bytes in invalid)
        {
            AtlasStructuralScanException exception = Assert.Throws<AtlasStructuralScanException>(
                () =>
                    AtlasStructuralScanJson.Parse(
                        bytes,
                        source,
                        AtlasDocumentRole.GlobalSave,
                        cancellationToken: TestContext.Current.CancellationToken
                    )
            );
            Assert.Equal(AtlasStructuralScanFailure.MalformedScanDocument, exception.Failure);
        }
    }

    [Fact]
    public void ParserRejectsRoleScalarTargetAndSourceMutations()
    {
        AtlasSaveReadResult scalarSource = ReadJson("0");
        string scalarText = Encoding.UTF8.GetString(
            AtlasStructuralScanner
                .Scan(
                    scalarSource,
                    AtlasDocumentRole.GlobalSave,
                    cancellationToken: TestContext.Current.CancellationToken
                )
                .CanonicalUtf8.Span
        );

        AssertSourceFailure(
            Encoding.UTF8.GetBytes(
                scalarText.Replace("\"global-save\"", "\"config-save\"", StringComparison.Ordinal)
            ),
            scalarSource,
            AtlasStructuralScanFailure.SourceMismatch
        );
        AssertSourceFailure(
            Encoding.UTF8.GetBytes(
                scalarText.Replace(
                    "\"scalarKind\":\"number\"",
                    "\"scalarKind\":\"text\"",
                    StringComparison.Ordinal
                )
            ),
            scalarSource,
            AtlasStructuralScanFailure.SourceMismatch
        );

        AtlasSaveReadResult largerSource = ReadJson("[0,1]");
        byte[] omitted = AtlasStructuralScanner
            .Scan(
                ReadJson("[0]"),
                AtlasDocumentRole.GlobalSave,
                cancellationToken: TestContext.Current.CancellationToken
            )
            .GetCanonicalUtf8Bytes();
        AssertSourceFailure(omitted, largerSource, AtlasStructuralScanFailure.CensusMismatch);

        const string referencesJson =
            "{\"one\":{\"@c\":1,\"v\":0},\"two\":{\"@c\":2,\"v\":0},"
            + "\"r1\":{\"@r\":1},\"r2\":{\"@r\":1},\"r3\":{\"@r\":2}}";
        AtlasSaveReadResult referenceSource = ReadJson(referencesJson);
        AtlasStructuralScanDocument referenceDocument = AtlasStructuralScanner
            .Scan(
                referenceSource,
                AtlasDocumentRole.GlobalSave,
                cancellationToken: TestContext.Current.CancellationToken
            )
            .Document;
        AtlasStructuralLocator secondIdentity = Assert
            .IsType<AtlasStructuralObjectObservation>(referenceDocument.Observations[3])
            .IdentityDefinitionLocator!;
        AtlasStructuralObservation[] mutatedObservations = [.. referenceDocument.Observations];
        AtlasStructuralReferenceObservation reference =
            Assert.IsType<AtlasStructuralReferenceObservation>(mutatedObservations[5]);
        mutatedObservations[5] = new AtlasStructuralReferenceObservation(
            reference.Locator,
            secondIdentity
        );
        AtlasStructuralScanDocument wrongTarget = new(
            referenceDocument.DocumentRole,
            referenceDocument.Census,
            mutatedObservations
        );
        byte[] wrongTargetBytes = AtlasStructuralScanJson.Serialize(
            wrongTarget,
            cancellationToken: TestContext.Current.CancellationToken
        );
        AssertSourceFailure(
            wrongTargetBytes,
            referenceSource,
            AtlasStructuralScanFailure.SourceMismatch
        );
    }

    [Fact]
    public void StructuralValidationRejectsAllCensusMutationsAndLocatorFaults()
    {
        AtlasStructuralScanDocument document = AtlasStructuralScanner
            .Scan(
                ReadJson("[0,1]"),
                AtlasDocumentRole.SlotSave,
                cancellationToken: TestContext.Current.CancellationToken
            )
            .Document;
        AtlasStructuralScanCensus census = document.Census;
        AtlasStructuralScanCensus[] mutations =
        [
            census with
            {
                NodeOccurrences = census.NodeOccurrences + 1,
            },
            census with
            {
                ObjectOccurrences = census.ObjectOccurrences + 1,
            },
            census with
            {
                ArrayOccurrences = census.ArrayOccurrences + 1,
            },
            census with
            {
                ScalarOccurrences = census.ScalarOccurrences + 1,
            },
            census with
            {
                ReferenceOccurrences = census.ReferenceOccurrences + 1,
            },
            census with
            {
                OrdinaryMemberEdges = census.OrdinaryMemberEdges + 1,
            },
            census with
            {
                ArrayElementEdges = census.ArrayElementEdges + 1,
            },
            census with
            {
                IdentityDefinitions = census.IdentityDefinitions + 1,
            },
            census with
            {
                ClassMarkers = census.ClassMarkers + 1,
            },
            census with
            {
                IdentityArrayWrappers = census.IdentityArrayWrappers + 1,
            },
            census with
            {
                DistinctReferencedDefinitions = census.DistinctReferencedDefinitions + 1,
            },
        ];

        foreach (AtlasStructuralScanCensus mutation in mutations)
        {
            AssertSerializationFailure(
                new AtlasStructuralScanDocument(
                    document.DocumentRole,
                    mutation,
                    document.Observations
                ),
                AtlasStructuralScanFailure.CensusMismatch
            );
        }

        AtlasStructuralObservation[] duplicate = [.. document.Observations];
        duplicate[2] = new AtlasStructuralScalarObservation(
            duplicate[1].Locator,
            AtlasStructuralScalarKind.Number
        );
        AssertSerializationFailure(
            new AtlasStructuralScanDocument(document.DocumentRole, document.Census, duplicate),
            AtlasStructuralScanFailure.DuplicateLocator
        );

        AtlasStructuralObservation[] wrongIndex = [.. document.Observations];
        wrongIndex[2] = new AtlasStructuralScalarObservation(
            new AtlasStructuralLocator(
                AtlasStructuralLocatorSubject.NodeOccurrence,
                [new AtlasArrayElementLocatorSegment(9)]
            ),
            AtlasStructuralScalarKind.Number
        );
        AssertSerializationFailure(
            new AtlasStructuralScanDocument(document.DocumentRole, document.Census, wrongIndex),
            AtlasStructuralScanFailure.InvalidLocator
        );
    }

    [Fact]
    public void SerializerAndParserHonorCanonicalAndRetainedBounds()
    {
        AtlasSaveReadResult source = ReadJson("[0]");
        AtlasStructuralScanResult scan = AtlasStructuralScanner.Scan(
            source,
            AtlasDocumentRole.GlobalSave,
            cancellationToken: TestContext.Current.CancellationToken
        );
        AtlasStructuralScannerLimits exact = new()
        {
            MaximumObservations = 2,
            MaximumLocatorDepth = 1,
            MaximumRetainedLocatorSegments = 1,
            MaximumCanonicalUtf8Bytes = scan.CanonicalUtf8.Length,
        };

        Assert.Equal(
            scan.GetCanonicalUtf8Bytes(),
            AtlasStructuralScanJson.Serialize(
                scan.Document,
                exact,
                TestContext.Current.CancellationToken
            )
        );
        _ = AtlasStructuralScanJson.Parse(
            scan.CanonicalUtf8,
            source,
            AtlasDocumentRole.GlobalSave,
            exact,
            TestContext.Current.CancellationToken
        );

        AtlasStructuralScanException bytes = Assert.Throws<AtlasStructuralScanException>(() =>
            AtlasStructuralScanJson.Parse(
                scan.CanonicalUtf8,
                source,
                AtlasDocumentRole.GlobalSave,
                exact with
                {
                    MaximumCanonicalUtf8Bytes = scan.CanonicalUtf8.Length - 1,
                },
                TestContext.Current.CancellationToken
            )
        );
        Assert.Equal(AtlasStructuralScanFailure.CanonicalSerializationLimit, bytes.Failure);

        AtlasStructuralScanException retained = Assert.Throws<AtlasStructuralScanException>(() =>
            AtlasStructuralScanJson.Parse(
                scan.CanonicalUtf8,
                source,
                AtlasDocumentRole.GlobalSave,
                exact with
                {
                    MaximumRetainedLocatorSegments = 0,
                },
                TestContext.Current.CancellationToken
            )
        );
        Assert.Equal(AtlasStructuralScanFailure.RetainedSegmentLimit, retained.Failure);
    }

    [Fact]
    public async Task SerializationAndParsingObserveCancellation()
    {
        AtlasSaveReadResult source = ReadJson("[0]");
        AtlasStructuralScanDocument document = AtlasStructuralScanner
            .Scan(
                source,
                AtlasDocumentRole.GlobalSave,
                cancellationToken: TestContext.Current.CancellationToken
            )
            .Document;
        byte[] bytes = AtlasStructuralScanJson.Serialize(
            document,
            cancellationToken: TestContext.Current.CancellationToken
        );
        using CancellationTokenSource canceled = new();
        await canceled.CancelAsync();

        Assert.ThrowsAny<OperationCanceledException>(() =>
            AtlasStructuralScanJson.Serialize(document, cancellationToken: canceled.Token)
        );
        Assert.ThrowsAny<OperationCanceledException>(() =>
            AtlasStructuralScanJson.Parse(
                bytes,
                source,
                AtlasDocumentRole.GlobalSave,
                cancellationToken: canceled.Token
            )
        );
    }

    [Fact]
    public void SchemaIsClosedDraft202012AndCoversCanonicalVariants()
    {
        string schemaPath = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "private",
            "app",
            "celesphonia-modifier",
            "docs",
            ".copilot",
            "schemas",
            "atlas-v0",
            "atlas-structural-scan.schema.json"
        );
        using JsonDocument schema = JsonDocument.Parse(File.ReadAllBytes(schemaPath));
        JsonElement root = schema.RootElement;

        Assert.Equal(
            "https://json-schema.org/draft/2020-12/schema",
            root.GetProperty("$schema").GetString()
        );
        Assert.False(root.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            "atlas-structural-scan/v1",
            root.GetProperty("properties")
                .GetProperty("schemaVersion")
                .GetProperty("const")
                .GetString()
        );
        JsonElement variants = root.GetProperty("properties")
            .GetProperty("observations")
            .GetProperty("items")
            .GetProperty("oneOf");
        Assert.Equal(6, variants.GetArrayLength());
        foreach (
            JsonProperty definition in root.GetProperty("$defs")
                .EnumerateObject()
                .Where(static property =>
                    property.Name.EndsWith("Observation", StringComparison.Ordinal)
                    || property.Name.EndsWith("Segment", StringComparison.Ordinal)
                    || property.Name.EndsWith("Locator", StringComparison.Ordinal)
                    || property.Name == "census"
                )
        )
        {
            Assert.False(definition.Value.GetProperty("additionalProperties").GetBoolean());
        }
    }

    private static void AssertSourceFailure(
        byte[] bytes,
        AtlasSaveReadResult source,
        AtlasStructuralScanFailure expected
    )
    {
        AtlasStructuralScanException exception = Assert.Throws<AtlasStructuralScanException>(() =>
            AtlasStructuralScanJson.Parse(
                bytes,
                source,
                AtlasDocumentRole.GlobalSave,
                cancellationToken: TestContext.Current.CancellationToken
            )
        );
        Assert.Equal(expected, exception.Failure);
    }

    private static void AssertSerializationFailure(
        AtlasStructuralScanDocument document,
        AtlasStructuralScanFailure expected
    )
    {
        AtlasStructuralScanException exception = Assert.Throws<AtlasStructuralScanException>(() =>
            AtlasStructuralScanJson.Serialize(
                document,
                cancellationToken: TestContext.Current.CancellationToken
            )
        );
        Assert.Equal(expected, exception.Failure);
    }

    private static AtlasSaveReadResult ReadJson(string json) =>
        AtlasSaveReader.Read(
            AtlasLzStringCodec.CompressToBase64(
                json,
                cancellationToken: TestContext.Current.CancellationToken
            ),
            cancellationToken: TestContext.Current.CancellationToken
        );

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "dirs.proj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
