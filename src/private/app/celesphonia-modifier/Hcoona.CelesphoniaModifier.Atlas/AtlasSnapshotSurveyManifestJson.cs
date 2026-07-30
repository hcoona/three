using System.Text.Json;

namespace Hcoona.CelesphoniaModifier.Atlas;

public static class AtlasSnapshotSurveyManifestJson
{
    private const int MaximumManifestTokens = 4096;

    public static byte[] Serialize(
        AtlasSnapshotSurveyManifest manifest,
        AtlasSnapshotSurveyLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        AtlasSnapshotSurveyLimits effectiveLimits =
            limits ?? AtlasSnapshotSurveyLimits.Default;
        effectiveLimits.Validate();
        Validate(manifest, effectiveLimits, cancellationToken);

        using BoundedMemoryStream stream = new(effectiveLimits.MaximumManifestBytes);
        using (
            Utf8JsonWriter writer = new(
                stream,
                new JsonWriterOptions { Indented = false, SkipValidation = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("schemaVersion", manifest.SchemaVersion);
            writer.WritePropertyName("documents");
            writer.WriteStartArray();
            foreach (AtlasSnapshotSurveyDocument document in manifest.Documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                writer.WriteStartObject();
                writer.WriteString(
                    "copiedSaveRelativePath",
                    document.CopiedSaveRelativePath);
                writer.WriteString(
                    "documentRole",
                    FormatDocumentRole(document.DocumentRole));
                writer.WriteString("scanRelativePath", document.ScanRelativePath);
                writer.WriteNumber(
                    "copiedSourceByteLength",
                    document.CopiedSourceByteLength);
                writer.WriteString("copiedSourceSha256", document.CopiedSourceSha256);
                writer.WriteNumber(
                    "persistedScanByteLength",
                    document.PersistedScanByteLength);
                writer.WriteString("persistedScanSha256", document.PersistedScanSha256);
                writer.WritePropertyName("census");
                WriteCensus(writer, document.Census);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WritePropertyName("totals");
            WriteTotals(writer, manifest.Totals);
            writer.WriteEndObject();
            writer.Flush();
        }

        cancellationToken.ThrowIfCancellationRequested();
        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    public static AtlasSnapshotSurveyManifest Parse(
        ReadOnlyMemory<byte> canonicalUtf8,
        AtlasSnapshotSurveyLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        AtlasSnapshotSurveyLimits effectiveLimits =
            limits ?? AtlasSnapshotSurveyLimits.Default;
        effectiveLimits.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (canonicalUtf8.Length > effectiveLimits.MaximumManifestBytes)
        {
            throw Malformed();
        }

        ReadOnlySpan<byte> input = canonicalUtf8.Span;
        if (input.Length == 0
            || input[^1] != (byte)'\n'
            || input.Length >= 3
                && input[0] == 0xEF
                && input[1] == 0xBB
                && input[2] == 0xBF)
        {
            throw Malformed();
        }

        try
        {
            AtlasSnapshotSurveyContracts.ValidateJsonEnvelope(
                input[..^1],
                MaximumManifestTokens,
                cancellationToken);
            Parser parser = new(input[..^1], effectiveLimits, cancellationToken);
            AtlasSnapshotSurveyManifest manifest = parser.Parse();
            Validate(manifest, effectiveLimits, cancellationToken);
            byte[] reserialized = Serialize(manifest, effectiveLimits, cancellationToken);
            if (!BytesEqual(reserialized, input, cancellationToken))
            {
                throw Malformed();
            }

            return manifest;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AtlasSafetyException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException
            or ArgumentException
            or InvalidOperationException
            or OverflowException)
        {
            throw Malformed(exception);
        }
    }

    internal static void Validate(
        AtlasSnapshotSurveyManifest manifest,
        AtlasSnapshotSurveyLimits limits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!StringComparer.Ordinal.Equals(
                manifest.SchemaVersion,
                AtlasSnapshotSurveyManifest.CurrentSchemaVersion)
            || manifest.Documents.Count is < 1
            || manifest.Documents.Count > limits.MaximumDocuments)
        {
            throw new AtlasSafetyException("The snapshot survey manifest is invalid.");
        }

        int priorOrder = -1;
        HashSet<string> scans = new(StringComparer.Ordinal);
        foreach (AtlasSnapshotSurveyDocument document in manifest.Documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (document is null
                || string.IsNullOrEmpty(document.CopiedSaveRelativePath)
                || string.IsNullOrEmpty(document.ScanRelativePath)
                || string.IsNullOrEmpty(document.CopiedSourceSha256)
                || string.IsNullOrEmpty(document.PersistedScanSha256)
                || !AtlasSaveSnapshot.TryGetCanonicalName(
                    document.CopiedSaveRelativePath,
                    out string canonicalName,
                    out int order)
                || !StringComparer.Ordinal.Equals(
                    canonicalName,
                    document.CopiedSaveRelativePath)
                || order <= priorOrder
                || document.DocumentRole != GetDocumentRole(canonicalName)
                || !StringComparer.Ordinal.Equals(
                    document.ScanRelativePath,
                    canonicalName + ".structural-scan.json")
                || !scans.Add(document.ScanRelativePath)
                || document.CopiedSourceByteLength < 0
                || document.PersistedScanByteLength < 0
                || document.Census is null
                || HasNegativeValue(document.Census))
            {
                throw new AtlasSafetyException("The snapshot survey manifest is invalid.");
            }

            AtlasSaveSnapshotContracts.ValidateLowerSha256(document.CopiedSourceSha256);
            AtlasSaveSnapshotContracts.ValidateLowerSha256(document.PersistedScanSha256);
            ValidateCensus(document.Census);
            priorOrder = order;
        }

        AtlasSnapshotSurveyTotals totals = CreateTotals(
            manifest.Documents,
            limits,
            cancellationToken);
        if (totals != manifest.Totals)
        {
            throw new AtlasSafetyException("The snapshot survey totals are invalid.");
        }
    }

    internal static AtlasSnapshotSurveyTotals CreateTotals(
        IEnumerable<AtlasSnapshotSurveyDocument> documents,
        AtlasSnapshotSurveyLimits limits,
        CancellationToken cancellationToken)
    {
        long documentCount = 0;
        long copiedSourceBytes = 0;
        long canonicalScanBytes = 0;
        long nodeOccurrences = 0;
        long objectOccurrences = 0;
        long arrayOccurrences = 0;
        long scalarOccurrences = 0;
        long referenceOccurrences = 0;
        long ordinaryMemberEdges = 0;
        long arrayElementEdges = 0;
        long identityDefinitions = 0;
        long classMarkers = 0;
        long identityArrayWrappers = 0;
        long distinctReferencedDefinitions = 0;
        try
        {
            foreach (AtlasSnapshotSurveyDocument document in documents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AtlasStructuralScanCensus census = document.Census;
                documentCount = checked(documentCount + 1);
                copiedSourceBytes = checked(
                    copiedSourceBytes + document.CopiedSourceByteLength);
                canonicalScanBytes = checked(
                    canonicalScanBytes + document.PersistedScanByteLength);
                nodeOccurrences = checked(
                    nodeOccurrences + census.NodeOccurrences);
                objectOccurrences = checked(
                    objectOccurrences + census.ObjectOccurrences);
                arrayOccurrences = checked(
                    arrayOccurrences + census.ArrayOccurrences);
                scalarOccurrences = checked(
                    scalarOccurrences + census.ScalarOccurrences);
                referenceOccurrences = checked(
                    referenceOccurrences + census.ReferenceOccurrences);
                ordinaryMemberEdges = checked(
                    ordinaryMemberEdges + census.OrdinaryMemberEdges);
                arrayElementEdges = checked(
                    arrayElementEdges + census.ArrayElementEdges);
                identityDefinitions = checked(
                    identityDefinitions + census.IdentityDefinitions);
                classMarkers = checked(classMarkers + census.ClassMarkers);
                identityArrayWrappers = checked(
                    identityArrayWrappers + census.IdentityArrayWrappers);
                distinctReferencedDefinitions = checked(
                    distinctReferencedDefinitions
                    + census.DistinctReferencedDefinitions);
            }
        }
        catch (OverflowException exception)
        {
            throw new AtlasSafetyException(
                "The snapshot survey aggregate arithmetic overflowed.",
                AtlasDiscoveryFailureStage.Unspecified,
                exception);
        }

        if (documentCount > limits.MaximumDocuments
            || nodeOccurrences > limits.MaximumObservations
            || canonicalScanBytes > limits.MaximumCanonicalScanBytes)
        {
            throw new AtlasSafetyException("The snapshot survey exceeds its aggregate limits.");
        }

        return new AtlasSnapshotSurveyTotals(
            documentCount,
            copiedSourceBytes,
            canonicalScanBytes,
            nodeOccurrences,
            objectOccurrences,
            arrayOccurrences,
            scalarOccurrences,
            referenceOccurrences,
            ordinaryMemberEdges,
            arrayElementEdges,
            identityDefinitions,
            classMarkers,
            identityArrayWrappers,
            distinctReferencedDefinitions);
    }

    internal static AtlasDocumentRole GetDocumentRole(string canonicalName) =>
        canonicalName switch
        {
            "global.rpgsave" => AtlasDocumentRole.GlobalSave,
            "config.rpgsave" => AtlasDocumentRole.ConfigSave,
            _ when canonicalName.StartsWith("file", StringComparison.Ordinal)
                => AtlasDocumentRole.SlotSave,
            _ => throw new AtlasSafetyException("The snapshot document role is invalid."),
        };

    private static void WriteCensus(
        Utf8JsonWriter writer,
        AtlasStructuralScanCensus census)
    {
        writer.WriteStartObject();
        writer.WriteNumber("nodeOccurrences", census.NodeOccurrences);
        writer.WriteNumber("objectOccurrences", census.ObjectOccurrences);
        writer.WriteNumber("arrayOccurrences", census.ArrayOccurrences);
        writer.WriteNumber("scalarOccurrences", census.ScalarOccurrences);
        writer.WriteNumber("referenceOccurrences", census.ReferenceOccurrences);
        writer.WriteNumber("ordinaryMemberEdges", census.OrdinaryMemberEdges);
        writer.WriteNumber("arrayElementEdges", census.ArrayElementEdges);
        writer.WriteNumber("identityDefinitions", census.IdentityDefinitions);
        writer.WriteNumber("classMarkers", census.ClassMarkers);
        writer.WriteNumber("identityArrayWrappers", census.IdentityArrayWrappers);
        writer.WriteNumber(
            "distinctReferencedDefinitions",
            census.DistinctReferencedDefinitions);
        writer.WriteEndObject();
    }

    private static bool HasNegativeValue(AtlasStructuralScanCensus census) =>
        census.NodeOccurrences < 0
        || census.ObjectOccurrences < 0
        || census.ArrayOccurrences < 0
        || census.ScalarOccurrences < 0
        || census.ReferenceOccurrences < 0
        || census.OrdinaryMemberEdges < 0
        || census.ArrayElementEdges < 0
        || census.IdentityDefinitions < 0
        || census.ClassMarkers < 0
        || census.IdentityArrayWrappers < 0
        || census.DistinctReferencedDefinitions < 0;

    private static void ValidateCensus(AtlasStructuralScanCensus census)
    {
        try
        {
            long variants = checked(
                census.ObjectOccurrences
                + census.ArrayOccurrences
                + census.ScalarOccurrences
                + census.ReferenceOccurrences);
            long edges = checked(census.OrdinaryMemberEdges + census.ArrayElementEdges);
            long identityContainers = checked(
                census.ObjectOccurrences + census.ArrayOccurrences);
            if (HasNegativeValue(census)
                || census.NodeOccurrences < 1
                || variants != census.NodeOccurrences
                || edges != census.NodeOccurrences - 1
                || census.IdentityArrayWrappers > census.ArrayOccurrences
                || census.IdentityDefinitions > identityContainers
                || census.ClassMarkers > census.ObjectOccurrences
                || census.DistinctReferencedDefinitions > census.IdentityDefinitions)
            {
                throw new AtlasSafetyException(
                    "A snapshot survey census is invalid.");
            }
        }
        catch (OverflowException exception)
        {
            throw new AtlasSafetyException(
                "A snapshot survey census overflowed.",
                AtlasDiscoveryFailureStage.Unspecified,
                exception);
        }
    }

    private static void WriteTotals(
        Utf8JsonWriter writer,
        AtlasSnapshotSurveyTotals totals)
    {
        writer.WriteStartObject();
        writer.WriteNumber("documentCount", totals.DocumentCount);
        writer.WriteNumber("copiedSourceBytes", totals.CopiedSourceBytes);
        writer.WriteNumber("canonicalScanBytes", totals.CanonicalScanBytes);
        writer.WriteNumber("nodeOccurrences", totals.NodeOccurrences);
        writer.WriteNumber("objectOccurrences", totals.ObjectOccurrences);
        writer.WriteNumber("arrayOccurrences", totals.ArrayOccurrences);
        writer.WriteNumber("scalarOccurrences", totals.ScalarOccurrences);
        writer.WriteNumber("referenceOccurrences", totals.ReferenceOccurrences);
        writer.WriteNumber("ordinaryMemberEdges", totals.OrdinaryMemberEdges);
        writer.WriteNumber("arrayElementEdges", totals.ArrayElementEdges);
        writer.WriteNumber("identityDefinitions", totals.IdentityDefinitions);
        writer.WriteNumber("classMarkers", totals.ClassMarkers);
        writer.WriteNumber("identityArrayWrappers", totals.IdentityArrayWrappers);
        writer.WriteNumber(
            "distinctReferencedDefinitions",
            totals.DistinctReferencedDefinitions);
        writer.WriteEndObject();
    }

    private static string FormatDocumentRole(AtlasDocumentRole role) =>
        role switch
        {
            AtlasDocumentRole.GlobalSave => "global-save",
            AtlasDocumentRole.ConfigSave => "config-save",
            AtlasDocumentRole.SlotSave => "slot-save",
            _ => throw new AtlasSafetyException("The snapshot document role is invalid."),
        };

    private static bool BytesEqual(
        ReadOnlySpan<byte> left,
        ReadOnlySpan<byte> right,
        CancellationToken cancellationToken)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int offset = 0; offset < left.Length; offset += 4096)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(4096, left.Length - offset);
            if (!left.Slice(offset, count).SequenceEqual(right.Slice(offset, count)))
            {
                return false;
            }
        }

        return true;
    }

    private static AtlasSafetyException Malformed(Exception? innerException = null) =>
        new(
            "The snapshot survey manifest is malformed.",
            AtlasDiscoveryFailureStage.Unspecified,
            innerException);

    private sealed class BoundedMemoryStream(int maximumBytes)
        : MemoryStream(Math.Min(maximumBytes, 16 * 1024))
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureCapacityFor(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureCapacityFor(buffer.Length);
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            EnsureCapacityFor(1);
            base.WriteByte(value);
        }

        private void EnsureCapacityFor(int count)
        {
            if (Length > maximumBytes - count)
            {
                throw new AtlasSafetyException(
                    "The snapshot survey manifest exceeds its byte limit.");
            }
        }
    }

    private ref struct Parser
    {
        private readonly AtlasSnapshotSurveyLimits limits;
        private readonly CancellationToken cancellationToken;
        private Utf8JsonReader reader;

        public Parser(
            ReadOnlySpan<byte> utf8,
            AtlasSnapshotSurveyLimits limits,
            CancellationToken cancellationToken)
        {
            this.limits = limits;
            this.cancellationToken = cancellationToken;
            reader = new Utf8JsonReader(
                utf8,
                new JsonReaderOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = AtlasSnapshotSurveyContracts.MaximumJsonDepth,
                });
        }

        public AtlasSnapshotSurveyManifest Parse()
        {
            RequireToken(JsonTokenType.StartObject);
            RequireProperty("schemaVersion");
            RequireString(AtlasSnapshotSurveyManifest.CurrentSchemaVersion);
            RequireProperty("documents");
            List<AtlasSnapshotSurveyDocument> documents = ParseDocuments();
            RequireProperty("totals");
            AtlasSnapshotSurveyTotals totals = ParseTotals();
            RequireToken(JsonTokenType.EndObject);
            if (ReadNext())
            {
                throw Malformed();
            }

            return new AtlasSnapshotSurveyManifest(documents, totals);
        }

        private List<AtlasSnapshotSurveyDocument> ParseDocuments()
        {
            RequireToken(JsonTokenType.StartArray);
            List<AtlasSnapshotSurveyDocument> documents = [];
            while (ReadNext() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.StartObject
                    || documents.Count >= limits.MaximumDocuments)
                {
                    throw Malformed();
                }

                documents.Add(ParseDocument());
            }

            if (reader.TokenType != JsonTokenType.EndArray)
            {
                throw Malformed();
            }

            return documents;
        }

        private AtlasSnapshotSurveyDocument ParseDocument()
        {
            RequireProperty("copiedSaveRelativePath");
            string source = ReadString();
            RequireProperty("documentRole");
            AtlasDocumentRole role = ParseDocumentRole();
            RequireProperty("scanRelativePath");
            string scan = ReadString();
            long sourceLength = ReadNamedNonnegativeInt64("copiedSourceByteLength");
            RequireProperty("copiedSourceSha256");
            string sourceHash = ReadString();
            long scanLength = ReadNamedNonnegativeInt64("persistedScanByteLength");
            RequireProperty("persistedScanSha256");
            string scanHash = ReadString();
            RequireProperty("census");
            AtlasStructuralScanCensus census = ParseCensus();
            RequireToken(JsonTokenType.EndObject);
            return new AtlasSnapshotSurveyDocument(
                source,
                role,
                scan,
                sourceLength,
                sourceHash,
                scanLength,
                scanHash,
                census);
        }

        private AtlasStructuralScanCensus ParseCensus()
        {
            RequireToken(JsonTokenType.StartObject);
            long nodes = ReadNamedNonnegativeInt64("nodeOccurrences");
            long objects = ReadNamedNonnegativeInt64("objectOccurrences");
            long arrays = ReadNamedNonnegativeInt64("arrayOccurrences");
            long scalars = ReadNamedNonnegativeInt64("scalarOccurrences");
            long references = ReadNamedNonnegativeInt64("referenceOccurrences");
            long ordinaryEdges = ReadNamedNonnegativeInt64("ordinaryMemberEdges");
            long arrayEdges = ReadNamedNonnegativeInt64("arrayElementEdges");
            long identities = ReadNamedNonnegativeInt64("identityDefinitions");
            long classMarkers = ReadNamedNonnegativeInt64("classMarkers");
            long identityArrays = ReadNamedNonnegativeInt64("identityArrayWrappers");
            long distinctReferences =
                ReadNamedNonnegativeInt64("distinctReferencedDefinitions");
            RequireToken(JsonTokenType.EndObject);
            return new AtlasStructuralScanCensus(
                nodes,
                objects,
                arrays,
                scalars,
                references,
                ordinaryEdges,
                arrayEdges,
                identities,
                classMarkers,
                identityArrays,
                distinctReferences);
        }

        private AtlasSnapshotSurveyTotals ParseTotals()
        {
            RequireToken(JsonTokenType.StartObject);
            long documentCount = ReadNamedNonnegativeInt64("documentCount");
            long sourceBytes = ReadNamedNonnegativeInt64("copiedSourceBytes");
            long scanBytes = ReadNamedNonnegativeInt64("canonicalScanBytes");
            long nodes = ReadNamedNonnegativeInt64("nodeOccurrences");
            long objects = ReadNamedNonnegativeInt64("objectOccurrences");
            long arrays = ReadNamedNonnegativeInt64("arrayOccurrences");
            long scalars = ReadNamedNonnegativeInt64("scalarOccurrences");
            long references = ReadNamedNonnegativeInt64("referenceOccurrences");
            long ordinaryEdges = ReadNamedNonnegativeInt64("ordinaryMemberEdges");
            long arrayEdges = ReadNamedNonnegativeInt64("arrayElementEdges");
            long identities = ReadNamedNonnegativeInt64("identityDefinitions");
            long classMarkers = ReadNamedNonnegativeInt64("classMarkers");
            long identityArrays = ReadNamedNonnegativeInt64("identityArrayWrappers");
            long distinctReferences =
                ReadNamedNonnegativeInt64("distinctReferencedDefinitions");
            RequireToken(JsonTokenType.EndObject);
            return new AtlasSnapshotSurveyTotals(
                documentCount,
                sourceBytes,
                scanBytes,
                nodes,
                objects,
                arrays,
                scalars,
                references,
                ordinaryEdges,
                arrayEdges,
                identities,
                classMarkers,
                identityArrays,
                distinctReferences);
        }

        private AtlasDocumentRole ParseDocumentRole()
        {
            RequireToken(JsonTokenType.String);
            if (reader.ValueTextEquals("global-save"u8))
            {
                return AtlasDocumentRole.GlobalSave;
            }

            if (reader.ValueTextEquals("config-save"u8))
            {
                return AtlasDocumentRole.ConfigSave;
            }

            if (reader.ValueTextEquals("slot-save"u8))
            {
                return AtlasDocumentRole.SlotSave;
            }

            throw Malformed();
        }

        private long ReadNamedNonnegativeInt64(string name)
        {
            RequireProperty(name);
            RequireToken(JsonTokenType.Number);
            if (!reader.TryGetInt64(out long value) || value < 0)
            {
                throw Malformed();
            }

            return value;
        }

        private string ReadString()
        {
            RequireToken(JsonTokenType.String);
            return reader.GetString() ?? throw Malformed();
        }

        private void RequireString(string expected)
        {
            RequireToken(JsonTokenType.String);
            if (!reader.ValueTextEquals(expected))
            {
                throw Malformed();
            }
        }

        private void RequireProperty(string name)
        {
            RequireToken(JsonTokenType.PropertyName);
            if (!reader.ValueTextEquals(name))
            {
                throw Malformed();
            }
        }

        private void RequireToken(JsonTokenType tokenType)
        {
            if (!ReadNext() || reader.TokenType != tokenType)
            {
                throw Malformed();
            }
        }

        private bool ReadNext()
        {
            cancellationToken.ThrowIfCancellationRequested();
            return reader.Read();
        }
    }
}
