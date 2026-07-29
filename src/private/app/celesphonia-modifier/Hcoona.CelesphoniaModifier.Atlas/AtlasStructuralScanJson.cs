using System.Collections.ObjectModel;
using System.Text.Json;

namespace Hcoona.CelesphoniaModifier.Atlas;

public static class AtlasStructuralScanJson
{
    private const int CancellationCheckInterval = 4096;
    private const int MaximumCanonicalStringTokenBytes = 64;
    private const int MaximumCanonicalNumberTokenBytes = 19;

    public static byte[] Serialize(
        AtlasStructuralScanDocument document,
        AtlasStructuralScannerLimits? limits = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(document);
        AtlasStructuralScannerLimits effectiveLimits =
            limits ?? AtlasStructuralScannerLimits.Default;
        effectiveLimits.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        AtlasStructuralScanValidator.ValidateStructure(
            document,
            effectiveLimits,
            cancellationToken
        );
        return SerializeValidated(document, effectiveLimits, cancellationToken);
    }

    public static AtlasStructuralScanResult Parse(
        ReadOnlyMemory<byte> canonicalUtf8,
        AtlasSaveReadResult expectedSource,
        AtlasDocumentRole expectedRole,
        AtlasStructuralScannerLimits? limits = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(expectedSource);
        AtlasStructuralScannerLimits effectiveLimits =
            limits ?? AtlasStructuralScannerLimits.Default;
        effectiveLimits.Validate();
        AtlasStructuralScanner.ValidateDocumentRole(expectedRole);
        cancellationToken.ThrowIfCancellationRequested();

        if (canonicalUtf8.Length > effectiveLimits.MaximumCanonicalUtf8Bytes)
        {
            throw new AtlasStructuralScanException(
                AtlasStructuralScanFailure.CanonicalSerializationLimit
            );
        }

        ReadOnlySpan<byte> input = canonicalUtf8.Span;
        if (
            input.Length == 0
            || input[^1] != (byte)'\n'
            || input.Length >= 3 && input[0] == 0xEF && input[1] == 0xBB && input[2] == 0xBF
        )
        {
            throw Malformed();
        }

        ValidateCanonicalLexicalBounds(input[..^1], cancellationToken);

        AtlasStructuralScanDocument document;
        try
        {
            Parser parser = new(input[..^1], effectiveLimits, cancellationToken);
            document = parser.Parse();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AtlasStructuralScanException)
        {
            throw;
        }
        catch (JsonException)
        {
            throw Malformed();
        }
        catch (InvalidOperationException)
        {
            throw Malformed();
        }

        AtlasStructuralScanValidator.ValidateAgainstSource(
            document,
            expectedSource,
            expectedRole,
            effectiveLimits,
            cancellationToken
        );
        byte[] reserialized = SerializeValidated(document, effectiveLimits, cancellationToken);
        if (!BytesEqual(reserialized, input, cancellationToken))
        {
            throw Malformed();
        }

        AtlasStructuralScanDocument expected = AtlasStructuralScanner.BuildDocument(
            expectedSource,
            expectedRole,
            effectiveLimits,
            cancellationToken
        );
        AtlasStructuralScanValidator.ValidateAgainstSource(
            expected,
            expectedSource,
            expectedRole,
            effectiveLimits,
            cancellationToken
        );
        if (!AtlasStructuralScanValidator.DocumentsEqual(document, expected, cancellationToken))
        {
            throw new AtlasStructuralScanException(AtlasStructuralScanFailure.SourceMismatch);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new AtlasStructuralScanResult(document, reserialized);
    }

    internal static byte[] SerializeValidated(
        AtlasStructuralScanDocument document,
        AtlasStructuralScannerLimits limits,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        using BoundedMemoryStream stream = new(limits.MaximumCanonicalUtf8Bytes);
        using (
            Utf8JsonWriter writer = new(
                stream,
                new JsonWriterOptions { Indented = false, SkipValidation = false }
            )
        )
        {
            writer.WriteStartObject();
            writer.WriteString("schemaVersion", document.SchemaVersion);
            writer.WriteString("documentRole", FormatDocumentRole(document.DocumentRole));
            writer.WritePropertyName("census");
            WriteCensus(writer, document.Census);
            writer.WritePropertyName("observations");
            writer.WriteStartArray();
            foreach (AtlasStructuralObservation observation in document.Observations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteObservation(writer, observation, cancellationToken);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }

        cancellationToken.ThrowIfCancellationRequested();
        stream.WriteByte((byte)'\n');
        return stream.ToArray(cancellationToken);
    }

    private static void WriteCensus(Utf8JsonWriter writer, AtlasStructuralScanCensus census)
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
        writer.WriteNumber("distinctReferencedDefinitions", census.DistinctReferencedDefinitions);
        writer.WriteEndObject();
    }

    private static void WriteObservation(
        Utf8JsonWriter writer,
        AtlasStructuralObservation observation,
        CancellationToken cancellationToken
    )
    {
        writer.WriteStartObject();
        writer.WritePropertyName("locator");
        WriteLocator(writer, observation.Locator, cancellationToken);
        switch (observation)
        {
            case AtlasStructuralScalarObservation scalar:
                writer.WriteString("kind", "scalar");
                writer.WriteString("scalarKind", FormatScalarKind(scalar.ScalarKind));
                break;
            case AtlasStructuralObjectObservation objectObservation:
                writer.WriteString("kind", "object");
                writer.WriteString("shape", FormatObjectShape(objectObservation.Shape));
                writer.WriteNumber("childCount", objectObservation.ChildCount);
                writer.WriteBoolean("classMarkerPresent", objectObservation.ClassMarkerPresent);
                writer.WriteBoolean(
                    "identityDefinitionPresent",
                    objectObservation.IdentityDefinitionPresent
                );
                if (objectObservation.IdentityDefinitionLocator is not null)
                {
                    writer.WritePropertyName("identityDefinitionLocator");
                    WriteLocator(
                        writer,
                        objectObservation.IdentityDefinitionLocator,
                        cancellationToken
                    );
                }

                break;
            case AtlasStructuralArrayObservation arrayObservation:
                writer.WriteString("kind", "array");
                writer.WriteString("shape", FormatArrayShape(arrayObservation.Shape));
                writer.WriteNumber("childCount", arrayObservation.ChildCount);
                writer.WriteBoolean(
                    "identityDefinitionPresent",
                    arrayObservation.IdentityDefinitionPresent
                );
                if (arrayObservation.IdentityDefinitionLocator is not null)
                {
                    writer.WritePropertyName("identityDefinitionLocator");
                    WriteLocator(
                        writer,
                        arrayObservation.IdentityDefinitionLocator,
                        cancellationToken
                    );
                }

                break;
            case AtlasStructuralReferenceObservation reference:
                writer.WriteString("kind", "reference");
                writer.WritePropertyName("targetIdentityDefinitionLocator");
                WriteLocator(writer, reference.TargetIdentityDefinitionLocator, cancellationToken);
                break;
            default:
                throw new AtlasStructuralScanException(
                    AtlasStructuralScanFailure.UnsupportedInternalState
                );
        }

        writer.WriteEndObject();
    }

    private static void WriteLocator(
        Utf8JsonWriter writer,
        AtlasStructuralLocator locator,
        CancellationToken cancellationToken
    )
    {
        writer.WriteStartObject();
        writer.WriteString("subject", FormatSubject(locator.Subject));
        writer.WritePropertyName("segments");
        writer.WriteStartArray();
        foreach (AtlasStructuralLocatorSegment segment in locator.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            writer.WriteStartObject();
            switch (segment)
            {
                case AtlasOrdinaryMemberLocatorSegment ordinary:
                    writer.WriteString("kind", "ordinary-member");
                    writer.WriteNumber("ordinal", ordinary.Ordinal);
                    break;
                case AtlasArrayElementLocatorSegment array:
                    writer.WriteString("kind", "array-element");
                    writer.WriteNumber("index", array.Index);
                    break;
                default:
                    throw new AtlasStructuralScanException(
                        AtlasStructuralScanFailure.UnsupportedInternalState
                    );
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    private static string FormatDocumentRole(AtlasDocumentRole role) =>
        role switch
        {
            AtlasDocumentRole.GlobalSave => "global-save",
            AtlasDocumentRole.ConfigSave => "config-save",
            AtlasDocumentRole.SlotSave => "slot-save",
            _ => throw new AtlasStructuralScanException(
                AtlasStructuralScanFailure.UnsupportedInternalState
            ),
        };

    private static string FormatSubject(AtlasStructuralLocatorSubject subject) =>
        subject switch
        {
            AtlasStructuralLocatorSubject.NodeOccurrence => "node-occurrence",
            AtlasStructuralLocatorSubject.ReferenceOccurrence => "reference-occurrence",
            AtlasStructuralLocatorSubject.IdentityDefinition => "identity-definition",
            _ => throw new AtlasStructuralScanException(
                AtlasStructuralScanFailure.UnsupportedInternalState
            ),
        };

    private static string FormatScalarKind(AtlasStructuralScalarKind scalarKind) =>
        scalarKind switch
        {
            AtlasStructuralScalarKind.Text => "text",
            AtlasStructuralScalarKind.Number => "number",
            AtlasStructuralScalarKind.True => "true",
            AtlasStructuralScalarKind.False => "false",
            AtlasStructuralScalarKind.Null => "null",
            _ => throw new AtlasStructuralScanException(
                AtlasStructuralScanFailure.UnsupportedInternalState
            ),
        };

    private static string FormatObjectShape(AtlasStructuralObjectShape shape) =>
        shape switch
        {
            AtlasStructuralObjectShape.PlainObject => "plain-object",
            AtlasStructuralObjectShape.IdentityObject => "identity-object",
            _ => throw new AtlasStructuralScanException(
                AtlasStructuralScanFailure.UnsupportedInternalState
            ),
        };

    private static string FormatArrayShape(AtlasStructuralArrayShape shape) =>
        shape switch
        {
            AtlasStructuralArrayShape.PlainArray => "plain-array",
            AtlasStructuralArrayShape.IdentityArrayWrapper => "identity-array-wrapper",
            _ => throw new AtlasStructuralScanException(
                AtlasStructuralScanFailure.UnsupportedInternalState
            ),
        };

    private static bool BytesEqual(
        ReadOnlySpan<byte> left,
        ReadOnlySpan<byte> right,
        CancellationToken cancellationToken
    )
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        const int chunkSize = 4096;
        for (int offset = 0; offset < left.Length; offset += chunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(chunkSize, left.Length - offset);
            if (!left.Slice(offset, count).SequenceEqual(right.Slice(offset, count)))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateCanonicalLexicalBounds(
        ReadOnlySpan<byte> input,
        CancellationToken cancellationToken
    )
    {
        bool inString = false;
        int stringLength = 0;
        int numberLength = 0;
        for (int index = 0; index < input.Length; index++)
        {
            if (index % CancellationCheckInterval == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            byte value = input[index];
            if (value > 0x7F)
            {
                throw Malformed();
            }

            if (inString)
            {
                if (value == (byte)'"')
                {
                    inString = false;
                    stringLength = 0;
                    continue;
                }

                if (
                    value == (byte)'\\'
                    || value < 0x20
                    || ++stringLength > MaximumCanonicalStringTokenBytes
                )
                {
                    throw Malformed();
                }

                continue;
            }

            if (value == (byte)'"')
            {
                inString = true;
                numberLength = 0;
                continue;
            }

            if (value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' || value < 0x20)
            {
                throw Malformed();
            }

            if (value is >= (byte)'0' and <= (byte)'9')
            {
                if (++numberLength > MaximumCanonicalNumberTokenBytes)
                {
                    throw Malformed();
                }

                continue;
            }

            numberLength = 0;
            if (
                value
                is not (byte)'{'
                    and not (byte)'}'
                    and not (byte)'['
                    and not (byte)']'
                    and not (byte)':'
                    and not (byte)','
                    and not (byte)'t'
                    and not (byte)'r'
                    and not (byte)'u'
                    and not (byte)'e'
                    and not (byte)'f'
                    and not (byte)'a'
                    and not (byte)'l'
                    and not (byte)'s'
            )
            {
                throw Malformed();
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (inString)
        {
            throw Malformed();
        }
    }

    private static AtlasStructuralScanException Malformed() =>
        new(AtlasStructuralScanFailure.MalformedScanDocument);

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

        public byte[] ToArray(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetBuffer(out ArraySegment<byte> buffer) || buffer.Array is null)
            {
                throw new InvalidOperationException("The canonical UTF-8 buffer is unavailable.");
            }

            int length = checked((int)Length);
            return AtlasCanonicalUtf8Bytes.Copy(
                buffer.Array.AsSpan(buffer.Offset, length),
                cancellationToken
            );
        }

        private void EnsureCapacityFor(int count)
        {
            if (Length > maximumBytes - count)
            {
                throw new AtlasStructuralScanException(
                    AtlasStructuralScanFailure.CanonicalSerializationLimit
                );
            }
        }
    }

    private ref struct Parser
    {
        private readonly AtlasStructuralScannerLimits limits;
        private readonly CancellationToken cancellationToken;
        private Utf8JsonReader reader;
        private long retainedSegments;

        public Parser(
            ReadOnlySpan<byte> utf8,
            AtlasStructuralScannerLimits limits,
            CancellationToken cancellationToken
        )
        {
            this.limits = limits;
            this.cancellationToken = cancellationToken;
            reader = new Utf8JsonReader(
                utf8,
                new JsonReaderOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = 16,
                }
            );
            retainedSegments = 0;
        }

        public AtlasStructuralScanDocument Parse()
        {
            RequireToken(JsonTokenType.StartObject);
            RequireProperty("schemaVersion");
            RequireString(AtlasStructuralScanDocument.CurrentSchemaVersion);
            RequireProperty("documentRole");
            AtlasDocumentRole role = ParseDocumentRole();
            RequireProperty("census");
            AtlasStructuralScanCensus census = ParseCensus();
            RequireProperty("observations");
            ReadOnlyCollection<AtlasStructuralObservation> observations = ParseObservations();
            RequireToken(JsonTokenType.EndObject);
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.Read())
            {
                throw Malformed();
            }

            return new AtlasStructuralScanDocument(role, census, observations);
        }

        private AtlasStructuralScanCensus ParseCensus()
        {
            RequireToken(JsonTokenType.StartObject);
            long nodeOccurrences = ReadNamedNonnegativeInt64("nodeOccurrences");
            long objectOccurrences = ReadNamedNonnegativeInt64("objectOccurrences");
            long arrayOccurrences = ReadNamedNonnegativeInt64("arrayOccurrences");
            long scalarOccurrences = ReadNamedNonnegativeInt64("scalarOccurrences");
            long referenceOccurrences = ReadNamedNonnegativeInt64("referenceOccurrences");
            long ordinaryMemberEdges = ReadNamedNonnegativeInt64("ordinaryMemberEdges");
            long arrayElementEdges = ReadNamedNonnegativeInt64("arrayElementEdges");
            long identityDefinitions = ReadNamedNonnegativeInt64("identityDefinitions");
            long classMarkers = ReadNamedNonnegativeInt64("classMarkers");
            long identityArrayWrappers = ReadNamedNonnegativeInt64("identityArrayWrappers");
            long distinctReferencedDefinitions = ReadNamedNonnegativeInt64(
                "distinctReferencedDefinitions"
            );
            RequireToken(JsonTokenType.EndObject);
            return new AtlasStructuralScanCensus(
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
                distinctReferencedDefinitions
            );
        }

        private ReadOnlyCollection<AtlasStructuralObservation> ParseObservations()
        {
            RequireToken(JsonTokenType.StartArray);
            List<AtlasStructuralObservation> observations = [];
            while (ReadNext() && reader.TokenType != JsonTokenType.EndArray)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (observations.Count >= limits.MaximumObservations)
                {
                    throw new AtlasStructuralScanException(
                        AtlasStructuralScanFailure.ObservationLimit
                    );
                }

                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    throw Malformed();
                }

                observations.Add(ParseObservation());
            }

            if (reader.TokenType != JsonTokenType.EndArray)
            {
                throw Malformed();
            }

            return observations.AsReadOnly();
        }

        private AtlasStructuralObservation ParseObservation()
        {
            RequireProperty("locator");
            AtlasStructuralLocator locator = ParseLocator();
            RequireProperty("kind");
            RequireToken(JsonTokenType.String);
            if (reader.ValueTextEquals("scalar"u8))
            {
                RequireProperty("scalarKind");
                AtlasStructuralScalarKind scalarKind = ParseScalarKind();
                RequireToken(JsonTokenType.EndObject);
                return new AtlasStructuralScalarObservation(locator, scalarKind);
            }

            if (reader.ValueTextEquals("object"u8))
            {
                RequireProperty("shape");
                AtlasStructuralObjectShape shape = ParseObjectShape();
                long childCount = ReadNamedNonnegativeInt64("childCount");
                bool classMarkerPresent = ReadNamedBoolean("classMarkerPresent");
                bool identityPresent = ReadNamedBoolean("identityDefinitionPresent");
                AtlasStructuralLocator? identity = null;
                if (identityPresent)
                {
                    RequireProperty("identityDefinitionLocator");
                    identity = ParseLocator();
                }

                RequireToken(JsonTokenType.EndObject);
                return new AtlasStructuralObjectObservation(
                    locator,
                    shape,
                    childCount,
                    classMarkerPresent,
                    identity
                );
            }

            if (reader.ValueTextEquals("array"u8))
            {
                RequireProperty("shape");
                AtlasStructuralArrayShape shape = ParseArrayShape();
                long childCount = ReadNamedNonnegativeInt64("childCount");
                bool identityPresent = ReadNamedBoolean("identityDefinitionPresent");
                AtlasStructuralLocator? identity = null;
                if (identityPresent)
                {
                    RequireProperty("identityDefinitionLocator");
                    identity = ParseLocator();
                }

                RequireToken(JsonTokenType.EndObject);
                return new AtlasStructuralArrayObservation(locator, shape, childCount, identity);
            }

            if (reader.ValueTextEquals("reference"u8))
            {
                RequireProperty("targetIdentityDefinitionLocator");
                AtlasStructuralLocator target = ParseLocator();
                RequireToken(JsonTokenType.EndObject);
                return new AtlasStructuralReferenceObservation(locator, target);
            }

            throw Malformed();
        }

        private AtlasStructuralLocator ParseLocator()
        {
            RequireToken(JsonTokenType.StartObject);
            RequireProperty("subject");
            AtlasStructuralLocatorSubject subject = ParseSubject();
            RequireProperty("segments");
            RequireToken(JsonTokenType.StartArray);
            List<AtlasStructuralLocatorSegment> segments = [];
            while (ReadNext() && reader.TokenType != JsonTokenType.EndArray)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (segments.Count >= limits.MaximumLocatorDepth)
                {
                    throw new AtlasStructuralScanException(
                        AtlasStructuralScanFailure.LocatorDepthLimit
                    );
                }

                if (reader.TokenType != JsonTokenType.StartObject)
                {
                    throw Malformed();
                }

                segments.Add(ParseSegment());
                AddRetainedSegment();
            }

            if (reader.TokenType != JsonTokenType.EndArray)
            {
                throw Malformed();
            }

            RequireToken(JsonTokenType.EndObject);
            return new AtlasStructuralLocator(subject, segments);
        }

        private AtlasStructuralLocatorSegment ParseSegment()
        {
            RequireProperty("kind");
            RequireToken(JsonTokenType.String);
            if (reader.ValueTextEquals("ordinary-member"u8))
            {
                long ordinal = ReadNamedNonnegativeInt64("ordinal");
                RequireToken(JsonTokenType.EndObject);
                return new AtlasOrdinaryMemberLocatorSegment(ordinal);
            }

            if (reader.ValueTextEquals("array-element"u8))
            {
                long index = ReadNamedNonnegativeInt64("index");
                RequireToken(JsonTokenType.EndObject);
                return new AtlasArrayElementLocatorSegment(index);
            }

            throw Malformed();
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

        private AtlasStructuralLocatorSubject ParseSubject()
        {
            RequireToken(JsonTokenType.String);
            if (reader.ValueTextEquals("node-occurrence"u8))
            {
                return AtlasStructuralLocatorSubject.NodeOccurrence;
            }

            if (reader.ValueTextEquals("reference-occurrence"u8))
            {
                return AtlasStructuralLocatorSubject.ReferenceOccurrence;
            }

            if (reader.ValueTextEquals("identity-definition"u8))
            {
                return AtlasStructuralLocatorSubject.IdentityDefinition;
            }

            throw Malformed();
        }

        private AtlasStructuralScalarKind ParseScalarKind()
        {
            RequireToken(JsonTokenType.String);
            if (reader.ValueTextEquals("text"u8))
            {
                return AtlasStructuralScalarKind.Text;
            }

            if (reader.ValueTextEquals("number"u8))
            {
                return AtlasStructuralScalarKind.Number;
            }

            if (reader.ValueTextEquals("true"u8))
            {
                return AtlasStructuralScalarKind.True;
            }

            if (reader.ValueTextEquals("false"u8))
            {
                return AtlasStructuralScalarKind.False;
            }

            if (reader.ValueTextEquals("null"u8))
            {
                return AtlasStructuralScalarKind.Null;
            }

            throw Malformed();
        }

        private AtlasStructuralObjectShape ParseObjectShape()
        {
            RequireToken(JsonTokenType.String);
            if (reader.ValueTextEquals("plain-object"u8))
            {
                return AtlasStructuralObjectShape.PlainObject;
            }

            if (reader.ValueTextEquals("identity-object"u8))
            {
                return AtlasStructuralObjectShape.IdentityObject;
            }

            throw Malformed();
        }

        private AtlasStructuralArrayShape ParseArrayShape()
        {
            RequireToken(JsonTokenType.String);
            if (reader.ValueTextEquals("plain-array"u8))
            {
                return AtlasStructuralArrayShape.PlainArray;
            }

            if (reader.ValueTextEquals("identity-array-wrapper"u8))
            {
                return AtlasStructuralArrayShape.IdentityArrayWrapper;
            }

            throw Malformed();
        }

        private long ReadNamedNonnegativeInt64(string propertyName)
        {
            RequireProperty(propertyName);
            RequireToken(JsonTokenType.Number);
            if (!reader.TryGetInt64(out long value) || value < 0)
            {
                throw Malformed();
            }

            return value;
        }

        private bool ReadNamedBoolean(string propertyName)
        {
            RequireProperty(propertyName);
            if (
                !ReadNext()
                || reader.TokenType is not JsonTokenType.True and not JsonTokenType.False
            )
            {
                throw Malformed();
            }

            return reader.GetBoolean();
        }

        private void RequireString(string value)
        {
            RequireToken(JsonTokenType.String);
            if (!reader.ValueTextEquals(value))
            {
                throw Malformed();
            }
        }

        private void RequireProperty(string propertyName)
        {
            RequireToken(JsonTokenType.PropertyName);
            if (!reader.ValueTextEquals(propertyName))
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

        private void AddRetainedSegment()
        {
            try
            {
                retainedSegments = checked(retainedSegments + 1);
            }
            catch (OverflowException)
            {
                throw new AtlasStructuralScanException(
                    AtlasStructuralScanFailure.RetainedSegmentLimit
                );
            }

            if (retainedSegments > limits.MaximumRetainedLocatorSegments)
            {
                throw new AtlasStructuralScanException(
                    AtlasStructuralScanFailure.RetainedSegmentLimit
                );
            }
        }
    }
}
