using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Hcoona.CelesphoniaModifier.Atlas;

public sealed record AtlasSaveReaderLimits
{
    public static AtlasSaveReaderLimits Default { get; } = new();

    public int MaximumEncodedBytes { get; init; } = 8 * 1024 * 1024;

    public int MaximumDecompressedCodeUnits { get; init; } = 32 * 1024 * 1024;

    public int MaximumJsonDepth { get; init; } = 256;

    public int MaximumJsonTokens { get; init; } = 2_000_000;

    public int MaximumScalarCodeUnits { get; init; } = 8 * 1024 * 1024;

    public int MaximumGraphNodes { get; init; } = 1_000_000;

    public int MaximumIdentityDefinitions { get; init; } = 250_000;

    public int MaximumReferenceOccurrences { get; init; } = 500_000;

    internal void Validate()
    {
        if (MaximumEncodedBytes < 1
            || MaximumDecompressedCodeUnits < 1
            || MaximumJsonDepth < 1
            || MaximumJsonTokens < 1
            || MaximumScalarCodeUnits < 1
            || MaximumGraphNodes < 1
            || MaximumIdentityDefinitions < 1
            || MaximumReferenceOccurrences < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AtlasSaveReaderLimits),
                "Reader limits must be positive.");
        }
    }
}

public enum AtlasSaveReadFailure
{
    InvalidCompressedAlphabetOrPadding,
    MalformedOrTruncatedCompressedInput,
    EncodedInputLimit,
    DecompressedSizeLimit,
    MalformedJson,
    JsonDepthLimit,
    JsonTokenLimit,
    ScalarSizeLimit,
    DuplicateIdentity,
    DanglingReference,
    InvalidMarkerType,
    InvalidArrayOrReferenceWrapper,
    GraphNodeLimit,
    IdentityCountLimit,
    ReferenceCountLimit,
    UnsupportedInternalState,
}

public sealed class AtlasSaveReadException : Exception
{
    public AtlasSaveReadException(AtlasSaveReadFailure failure, Exception? innerException = null)
        : base(GetMessage(failure), innerException)
    {
        Failure = failure;
    }

    public AtlasSaveReadFailure Failure { get; }

    private static string GetMessage(AtlasSaveReadFailure failure) =>
        failure switch
        {
            AtlasSaveReadFailure.InvalidCompressedAlphabetOrPadding =>
                "The compressed save has invalid alphabet or padding.",
            AtlasSaveReadFailure.MalformedOrTruncatedCompressedInput =>
                "The compressed save is malformed or truncated.",
            AtlasSaveReadFailure.EncodedInputLimit =>
                "The compressed save exceeds its encoded size limit.",
            AtlasSaveReadFailure.DecompressedSizeLimit =>
                "The decompressed save exceeds its size limit.",
            AtlasSaveReadFailure.MalformedJson => "The save JSON is malformed.",
            AtlasSaveReadFailure.JsonDepthLimit => "The save JSON exceeds its depth limit.",
            AtlasSaveReadFailure.JsonTokenLimit => "The save JSON exceeds its token limit.",
            AtlasSaveReadFailure.ScalarSizeLimit => "A save JSON scalar exceeds its size limit.",
            AtlasSaveReadFailure.DuplicateIdentity =>
                "The JsonEx graph contains a duplicate identity.",
            AtlasSaveReadFailure.DanglingReference =>
                "The JsonEx graph contains a dangling reference.",
            AtlasSaveReadFailure.InvalidMarkerType =>
                "The JsonEx graph contains an invalid marker.",
            AtlasSaveReadFailure.InvalidArrayOrReferenceWrapper =>
                "The JsonEx graph contains an invalid wrapper.",
            AtlasSaveReadFailure.GraphNodeLimit =>
                "The JsonEx graph exceeds its node limit.",
            AtlasSaveReadFailure.IdentityCountLimit =>
                "The JsonEx graph exceeds its identity limit.",
            AtlasSaveReadFailure.ReferenceCountLimit =>
                "The JsonEx graph exceeds its reference limit.",
            _ => "The save reader reached an unsupported internal state.",
        };
}

public readonly record struct AtlasJsonSourceSpan(long Start, long Length);

public enum AtlasJsonScalarKind
{
    Text,
    Number,
    True,
    False,
    Null,
}

public abstract record AtlasLosslessJsonValue(AtlasJsonSourceSpan Span);

public sealed record AtlasLosslessJsonObject(
    IReadOnlyList<AtlasLosslessJsonMember> Members,
    AtlasJsonSourceSpan Span)
    : AtlasLosslessJsonValue(Span);

public sealed record AtlasLosslessJsonArray(
    IReadOnlyList<AtlasLosslessJsonValue> Elements,
    AtlasJsonSourceSpan Span)
    : AtlasLosslessJsonValue(Span);

public sealed record AtlasLosslessJsonScalar(
    AtlasJsonScalarKind Kind,
    string RawLexeme,
    string? StringValue,
    AtlasJsonSourceSpan Span)
    : AtlasLosslessJsonValue(Span);

public sealed record AtlasLosslessJsonMember(
    string Name,
    string RawNameLexeme,
    AtlasJsonSourceSpan NameSpan,
    AtlasLosslessJsonValue Value);

public sealed record AtlasLosslessJsonDocument(
    ReadOnlyMemory<byte> Utf8Source,
    AtlasLosslessJsonValue Root);

public sealed record AtlasTokenCensus(
    long Containers,
    long MemberOccurrences,
    long ArrayElements,
    long Scalars,
    long IdentityMarkers,
    long ClassMarkers,
    long ArrayMarkers,
    long ReferenceMarkers);

public abstract class AtlasJsonExNode
{
    private protected AtlasJsonExNode(AtlasLosslessJsonValue syntax)
    {
        Syntax = syntax;
    }

    public AtlasLosslessJsonValue Syntax { get; }
}

public sealed class AtlasJsonExScalar(AtlasLosslessJsonScalar syntax)
    : AtlasJsonExNode(syntax)
{
    public AtlasLosslessJsonScalar Scalar => syntax;
}

public sealed class AtlasJsonExObject(
    AtlasLosslessJsonObject syntax,
    int? identity,
    string? opaqueClass)
    : AtlasJsonExNode(syntax)
{
    private readonly List<AtlasJsonExMember> members = [];

    public int? Identity { get; } = identity;

    public string? OpaqueClass { get; } = opaqueClass;

    public IReadOnlyList<AtlasJsonExMember> Members => members;

    internal List<AtlasJsonExMember> MutableMembers => members;
}

public sealed class AtlasJsonExArray(
    AtlasLosslessJsonValue syntax,
    int? identity)
    : AtlasJsonExNode(syntax)
{
    private readonly List<AtlasJsonExNode> elements = [];

    public int? Identity { get; } = identity;

    public IReadOnlyList<AtlasJsonExNode> Elements => elements;

    internal List<AtlasJsonExNode> MutableElements => elements;
}

public sealed class AtlasJsonExReference(
    AtlasLosslessJsonObject syntax,
    int referencedIdentity)
    : AtlasJsonExNode(syntax)
{
    public int ReferencedIdentity { get; } = referencedIdentity;

    public AtlasJsonExNode Target { get; internal set; } = null!;
}

public sealed record AtlasJsonExMember(
    string Name,
    string RawNameLexeme,
    AtlasJsonExNode Value);

public sealed record AtlasGraphCensus(
    long MaterializedNodes,
    long IdentityDefinitions,
    long ReferenceEdges,
    long SharedTargets,
    long Cycles);

public static class AtlasCensusReconciliation
{
    public static bool IsConsistent(
        AtlasTokenCensus tokenCensus,
        AtlasGraphCensus graphCensus)
    {
        if (tokenCensus.IdentityMarkers != graphCensus.IdentityDefinitions
            || tokenCensus.ReferenceMarkers != graphCensus.ReferenceEdges
            || tokenCensus.ArrayMarkers > tokenCensus.IdentityMarkers
            || tokenCensus.ClassMarkers > tokenCensus.IdentityMarkers)
        {
            return false;
        }

        try
        {
            long expectedMaterializedNodes = checked(
                tokenCensus.Containers
                + tokenCensus.Scalars
                - tokenCensus.IdentityMarkers
                - tokenCensus.ClassMarkers
                - tokenCensus.ArrayMarkers
                - tokenCensus.ReferenceMarkers);
            return expectedMaterializedNodes >= 0
                && graphCensus.MaterializedNodes == expectedMaterializedNodes;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}

public sealed class AtlasSaveReadResult
{
    internal AtlasSaveReadResult(
        byte[] originalCompressedBytes,
        AtlasLosslessJsonDocument json,
        AtlasJsonExNode graph,
        AtlasTokenCensus tokenCensus,
        AtlasGraphCensus graphCensus)
    {
        OriginalCompressedBytes = originalCompressedBytes;
        Json = json;
        Graph = graph;
        TokenCensus = tokenCensus;
        GraphCensus = graphCensus;
    }

    public ReadOnlyMemory<byte> OriginalCompressedBytes { get; }

    public AtlasLosslessJsonDocument Json { get; }

    public AtlasJsonExNode Graph { get; }

    public AtlasTokenCensus TokenCensus { get; }

    public AtlasGraphCensus GraphCensus { get; }

    public byte[] GetSemanticNoOpBytes() => OriginalCompressedBytes.ToArray();
}

public static class AtlasSaveReader
{
    public static AtlasSaveReadResult Read(
        ReadOnlyMemory<byte> compressedBytes,
        AtlasSaveReaderLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        AtlasSaveReaderLimits effectiveLimits = limits ?? AtlasSaveReaderLimits.Default;
        effectiveLimits.Validate();
        if (compressedBytes.Length > effectiveLimits.MaximumEncodedBytes)
        {
            throw new AtlasSaveReadException(AtlasSaveReadFailure.EncodedInputLimit);
        }

        byte[] original = compressedBytes.ToArray();
        string jsonText;
        try
        {
            jsonText = AtlasLzStringCodec.DecompressFromBase64(
                original,
                effectiveLimits,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AtlasLzStringException exception)
        {
            throw new AtlasSaveReadException(MapCodecFailure(exception.Failure), exception);
        }

        (AtlasLosslessJsonDocument document, AtlasTokenCensus tokenCensus) =
            ParseJson(jsonText, effectiveLimits, cancellationToken);
        JsonExGraphBuilder graphBuilder = new(effectiveLimits, cancellationToken);
        AtlasJsonExNode graph = graphBuilder.Build(document.Root);
        AtlasGraphCensus graphCensus = graphBuilder.CreateCensus(graph);
        if (!AtlasCensusReconciliation.IsConsistent(tokenCensus, graphCensus))
        {
            throw new AtlasSaveReadException(
                AtlasSaveReadFailure.UnsupportedInternalState);
        }

        return new AtlasSaveReadResult(
            original,
            document,
            graph,
            tokenCensus,
            graphCensus);
    }

    public static async ValueTask<AtlasSaveReadResult> ReadAsync(
        Stream source,
        AtlasSaveReaderLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new NotSupportedException("The source stream does not support reading.");
        }

        AtlasSaveReaderLimits effectiveLimits = limits ?? AtlasSaveReaderLimits.Default;
        effectiveLimits.Validate();
        using MemoryStream bytes = new(
            Math.Min(effectiveLimits.MaximumEncodedBytes, 64 * 1024));
        byte[] buffer = new byte[8192];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (bytes.Length > effectiveLimits.MaximumEncodedBytes - read)
            {
                throw new AtlasSaveReadException(AtlasSaveReadFailure.EncodedInputLimit);
            }

            bytes.Write(buffer, 0, read);
        }

        return Read(
            bytes.GetBuffer().AsMemory(0, checked((int)bytes.Length)),
            effectiveLimits,
            cancellationToken);
    }

    private static AtlasSaveReadFailure MapCodecFailure(AtlasLzStringFailure failure) =>
        failure switch
        {
            AtlasLzStringFailure.InvalidAlphabet or AtlasLzStringFailure.InvalidPadding =>
                AtlasSaveReadFailure.InvalidCompressedAlphabetOrPadding,
            AtlasLzStringFailure.EncodedInputLimit =>
                AtlasSaveReadFailure.EncodedInputLimit,
            AtlasLzStringFailure.DecompressedSizeLimit =>
                AtlasSaveReadFailure.DecompressedSizeLimit,
            AtlasLzStringFailure.MalformedOrTruncated =>
                AtlasSaveReadFailure.MalformedOrTruncatedCompressedInput,
            _ => AtlasSaveReadFailure.UnsupportedInternalState,
        };

    private static (
        AtlasLosslessJsonDocument Document,
        AtlasTokenCensus Census) ParseJson(
        string jsonText,
        AtlasSaveReaderLimits limits,
        CancellationToken cancellationToken)
    {
        byte[] utf8;
        try
        {
            utf8 = new UTF8Encoding(false, true).GetBytes(jsonText);
        }
        catch (EncoderFallbackException exception)
        {
            throw new AtlasSaveReadException(AtlasSaveReadFailure.MalformedJson, exception);
        }

        try
        {
            LosslessJsonParser parser = new(utf8, limits, cancellationToken);
            AtlasLosslessJsonValue root = parser.Parse();
            return (
                new AtlasLosslessJsonDocument(utf8, root),
                parser.CreateCensus());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AtlasSaveReadException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new AtlasSaveReadException(
                AtlasSaveReadFailure.MalformedJson,
                exception);
        }
    }

    private ref struct LosslessJsonParser
    {
        private readonly byte[] utf8;
        private readonly AtlasSaveReaderLimits limits;
        private readonly CancellationToken cancellationToken;
        private Utf8JsonReader reader;
        private long tokenCount;
        private long containers;
        private long memberOccurrences;
        private long arrayElements;
        private long scalars;
        private long identityMarkers;
        private long classMarkers;
        private long arrayMarkers;
        private long referenceMarkers;
        private int currentDepth;

        public LosslessJsonParser(
            byte[] utf8,
            AtlasSaveReaderLimits limits,
            CancellationToken cancellationToken)
        {
            this.utf8 = utf8;
            this.limits = limits;
            this.cancellationToken = cancellationToken;
            reader = new Utf8JsonReader(
                utf8,
                new JsonReaderOptions
                {
                    AllowTrailingCommas = false,
                    CommentHandling = JsonCommentHandling.Disallow,
                    MaxDepth = int.MaxValue,
                });
        }

        public AtlasLosslessJsonValue Parse()
        {
            if (!ReadNext())
            {
                throw new JsonException("The JSON document is empty.");
            }

            AtlasLosslessJsonValue value = ParseValue();
            if (ReadNext())
            {
                throw new JsonException("The JSON document has trailing data.");
            }

            return value;
        }

        public AtlasTokenCensus CreateCensus() =>
            new(
                containers,
                memberOccurrences,
                arrayElements,
                scalars,
                identityMarkers,
                classMarkers,
                arrayMarkers,
                referenceMarkers);

        private AtlasLosslessJsonValue ParseValue()
        {
            cancellationToken.ThrowIfCancellationRequested();
            return reader.TokenType switch
            {
                JsonTokenType.StartObject => ParseObject(),
                JsonTokenType.StartArray => ParseArray(),
                JsonTokenType.String => ParseStringScalar(),
                JsonTokenType.Number => ParseScalar(AtlasJsonScalarKind.Number, null),
                JsonTokenType.True => ParseScalar(AtlasJsonScalarKind.True, null),
                JsonTokenType.False => ParseScalar(AtlasJsonScalarKind.False, null),
                JsonTokenType.Null => ParseScalar(AtlasJsonScalarKind.Null, null),
                _ => throw new JsonException("A JSON value was expected."),
            };
        }

        private AtlasLosslessJsonObject ParseObject()
        {
            long start = reader.TokenStartIndex;
            containers++;
            List<AtlasLosslessJsonMember> members = [];
            while (ReadNext() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("A JSON property was expected.");
                }

                long nameStart = reader.TokenStartIndex;
                long nameEnd = GetPropertyNameEnd(nameStart, reader.BytesConsumed);
                ValidateRawStringToken(nameStart, nameEnd);
                string name = GetDecodedString();
                ValidateScalarLength(name.Length);
                string rawName = GetRawToken(nameStart, nameEnd);
                memberOccurrences++;
                CountMarker(name);
                if (!ReadNext())
                {
                    throw new JsonException("A JSON property value is missing.");
                }

                members.Add(
                    new AtlasLosslessJsonMember(
                        name,
                        rawName,
                        new AtlasJsonSourceSpan(nameStart, nameEnd - nameStart),
                        ParseValue()));
            }

            if (reader.TokenType != JsonTokenType.EndObject)
            {
                throw new JsonException("A JSON object is incomplete.");
            }

            return new AtlasLosslessJsonObject(
                members.AsReadOnly(),
                new AtlasJsonSourceSpan(start, reader.BytesConsumed - start));
        }

        private AtlasLosslessJsonArray ParseArray()
        {
            long start = reader.TokenStartIndex;
            containers++;
            List<AtlasLosslessJsonValue> elements = [];
            while (ReadNext() && reader.TokenType != JsonTokenType.EndArray)
            {
                arrayElements++;
                elements.Add(ParseValue());
            }

            if (reader.TokenType != JsonTokenType.EndArray)
            {
                throw new JsonException("A JSON array is incomplete.");
            }

            return new AtlasLosslessJsonArray(
                elements.AsReadOnly(),
                new AtlasJsonSourceSpan(start, reader.BytesConsumed - start));
        }

        private AtlasLosslessJsonScalar ParseScalar(
            AtlasJsonScalarKind kind,
            string? stringValue)
        {
            long start = reader.TokenStartIndex;
            ValidateRawToken(start, reader.BytesConsumed);
            string raw = GetRawToken(start, reader.BytesConsumed);
            ValidateScalarLength(raw.Length);
            scalars++;
            return new AtlasLosslessJsonScalar(
                kind,
                raw,
                stringValue,
                new AtlasJsonSourceSpan(start, reader.BytesConsumed - start));
        }

        private AtlasLosslessJsonScalar ParseStringScalar()
        {
            long start = reader.TokenStartIndex;
            long end = reader.BytesConsumed;
            ValidateRawStringToken(start, end);
            string value = GetDecodedString();
            ValidateScalarLength(value.Length);
            string raw = GetRawToken(start, end);
            scalars++;
            return new AtlasLosslessJsonScalar(
                AtlasJsonScalarKind.Text,
                raw,
                value,
                new AtlasJsonSourceSpan(start, end - start));
        }

        private string GetDecodedString()
        {
            try
            {
                return reader.GetString()
                    ?? throw new JsonException("A JSON string is invalid.");
            }
            catch (InvalidOperationException exception)
            {
                throw new JsonException("A JSON string is invalid.", exception);
            }
        }

        private bool ReadNext()
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool result = reader.Read();
            if (result)
            {
                if (++tokenCount > limits.MaximumJsonTokens)
                {
                    throw new AtlasSaveReadException(AtlasSaveReadFailure.JsonTokenLimit);
                }

                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    if (++currentDepth > limits.MaximumJsonDepth)
                    {
                        throw new AtlasSaveReadException(
                            AtlasSaveReadFailure.JsonDepthLimit);
                    }
                }
                else if (reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)
                {
                    currentDepth--;
                }
            }

            return result;
        }

        private long GetPropertyNameEnd(long start, long end)
        {
            int index = checked((int)end) - 1;
            while (index >= start && IsJsonWhitespace(utf8[index]))
            {
                index--;
            }

            if (index < start || utf8[index] != (byte)':')
            {
                throw new JsonException("A JSON property delimiter is invalid.");
            }

            index--;
            while (index >= start && IsJsonWhitespace(utf8[index]))
            {
                index--;
            }

            return index + 1L;
        }

        private static bool IsJsonWhitespace(byte value) =>
            value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';

        private string GetRawToken(long start, long end) =>
            Encoding.UTF8.GetString(
                utf8,
                checked((int)start),
                checked((int)(end - start)));

        private void ValidateRawStringToken(long start, long end)
        {
            long valueByteLength = reader.HasValueSequence
                ? reader.ValueSequence.Length
                : reader.ValueSpan.Length;
            if (valueByteLength > (long)limits.MaximumScalarCodeUnits * 3)
            {
                throw new AtlasSaveReadException(AtlasSaveReadFailure.ScalarSizeLimit);
            }

            int tokenStart = checked((int)start);
            int tokenLength = checked((int)(end - start));
            if (tokenLength < 2
                || utf8[tokenStart] != (byte)'"'
                || utf8[tokenStart + tokenLength - 1] != (byte)'"')
            {
                throw new JsonException("A JSON string token is invalid.");
            }

            ValidateRawUtf16Length(
                utf8.AsSpan(tokenStart + 1, tokenLength - 2));
        }

        private void ValidateRawToken(long start, long end)
        {
            int tokenStart = checked((int)start);
            int tokenLength = checked((int)(end - start));
            ValidateRawUtf16Length(utf8.AsSpan(tokenStart, tokenLength));
        }

        private void ValidateRawUtf16Length(ReadOnlySpan<byte> rawUtf8)
        {
            int codeUnits = 0;
            int offset = 0;
            while (offset < rawUtf8.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte first = rawUtf8[offset];
                int consumed;
                int addedCodeUnits;
                if (first < 0x80)
                {
                    consumed = 1;
                    addedCodeUnits = 1;
                }
                else
                {
                    OperationStatus status = Rune.DecodeFromUtf8(
                        rawUtf8[offset..],
                        out Rune rune,
                        out consumed);
                    if (status != OperationStatus.Done)
                    {
                        throw new JsonException("A JSON scalar has invalid UTF-8.");
                    }

                    addedCodeUnits = rune.Utf16SequenceLength;
                }

                if (codeUnits > limits.MaximumScalarCodeUnits - addedCodeUnits)
                {
                    throw new AtlasSaveReadException(
                        AtlasSaveReadFailure.ScalarSizeLimit);
                }

                codeUnits += addedCodeUnits;
                offset += consumed;
            }
        }

        private void ValidateScalarLength(int length)
        {
            if (length > limits.MaximumScalarCodeUnits)
            {
                throw new AtlasSaveReadException(AtlasSaveReadFailure.ScalarSizeLimit);
            }
        }

        private void CountMarker(string name)
        {
            switch (name)
            {
                case "@c":
                    identityMarkers++;
                    break;
                case "@":
                    classMarkers++;
                    break;
                case "@a":
                    arrayMarkers++;
                    break;
                case "@r":
                    referenceMarkers++;
                    break;
            }
        }
    }

    private sealed class JsonExGraphBuilder(
        AtlasSaveReaderLimits limits,
        CancellationToken cancellationToken)
    {
        private readonly Dictionary<int, AtlasJsonExNode> identities = [];
        private readonly List<AtlasJsonExReference> references = [];
        private long materializedNodes;

        public AtlasJsonExNode Build(AtlasLosslessJsonValue root)
        {
            AtlasJsonExNode graph = BuildNode(root);
            foreach (AtlasJsonExReference reference in references)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!identities.TryGetValue(
                        reference.ReferencedIdentity,
                        out AtlasJsonExNode? target))
                {
                    throw new AtlasSaveReadException(
                        AtlasSaveReadFailure.DanglingReference);
                }

                reference.Target = target;
            }

            return graph;
        }

        public AtlasGraphCensus CreateCensus(AtlasJsonExNode root)
        {
            cancellationToken.ThrowIfCancellationRequested();
            HashSet<AtlasJsonExNode> sharedTargets =
            [
                .. references.Select(static reference => reference.Target),
            ];
            long cycles = CountCycles(root);
            return new AtlasGraphCensus(
                materializedNodes,
                identities.Count,
                references.Count,
                sharedTargets.Count,
                cycles);
        }

        private AtlasJsonExNode BuildNode(AtlasLosslessJsonValue syntax)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return syntax switch
            {
                AtlasLosslessJsonScalar scalar => AddNode(new AtlasJsonExScalar(scalar)),
                AtlasLosslessJsonArray array => BuildPlainArray(array),
                AtlasLosslessJsonObject value => BuildObject(value),
                _ => throw new AtlasSaveReadException(
                    AtlasSaveReadFailure.UnsupportedInternalState),
            };
        }

        private AtlasJsonExArray BuildPlainArray(AtlasLosslessJsonArray syntax)
        {
            AtlasJsonExArray result = AddNode(new AtlasJsonExArray(syntax, null));
            foreach (AtlasLosslessJsonValue element in syntax.Elements)
            {
                result.MutableElements.Add(BuildNode(element));
            }

            return result;
        }

        private AtlasJsonExNode BuildObject(AtlasLosslessJsonObject syntax)
        {
            List<AtlasLosslessJsonMember> classMarkers =
                GetMembers(syntax, "@");
            List<AtlasLosslessJsonMember> identityMarkers =
                GetMembers(syntax, "@c");
            List<AtlasLosslessJsonMember> arrayMarkers =
                GetMembers(syntax, "@a");
            List<AtlasLosslessJsonMember> referenceMarkers =
                GetMembers(syntax, "@r");
            if (classMarkers.Count > 1
                || identityMarkers.Count > 1
                || arrayMarkers.Count > 1
                || referenceMarkers.Count > 1)
            {
                throw new AtlasSaveReadException(AtlasSaveReadFailure.InvalidMarkerType);
            }

            if (referenceMarkers.Count == 1)
            {
                if (syntax.Members.Count != 1
                    || classMarkers.Count != 0
                    || identityMarkers.Count != 0
                    || arrayMarkers.Count != 0)
                {
                    throw new AtlasSaveReadException(
                        AtlasSaveReadFailure.InvalidArrayOrReferenceWrapper);
                }

                int identity = ParseIdentity(referenceMarkers[0].Value);
                if (references.Count >= limits.MaximumReferenceOccurrences)
                {
                    throw new AtlasSaveReadException(
                        AtlasSaveReadFailure.ReferenceCountLimit);
                }

                AtlasJsonExReference reference = AddNode(
                    new AtlasJsonExReference(syntax, identity));
                references.Add(reference);
                return reference;
            }

            if (arrayMarkers.Count == 1)
            {
                if (syntax.Members.Count != 2
                    || identityMarkers.Count != 1
                    || classMarkers.Count != 0
                    || referenceMarkers.Count != 0
                    || arrayMarkers[0].Value is not AtlasLosslessJsonArray array)
                {
                    throw new AtlasSaveReadException(
                        AtlasSaveReadFailure.InvalidArrayOrReferenceWrapper);
                }

                int identity = ParseIdentity(identityMarkers[0].Value);
                AtlasJsonExArray result = AddNode(new AtlasJsonExArray(syntax, identity));
                RegisterIdentity(identity, result);
                foreach (AtlasLosslessJsonValue element in array.Elements)
                {
                    result.MutableElements.Add(BuildNode(element));
                }

                return result;
            }

            if (identityMarkers.Count == 1)
            {
                if (referenceMarkers.Count != 0 || arrayMarkers.Count != 0)
                {
                    throw new AtlasSaveReadException(
                        AtlasSaveReadFailure.InvalidArrayOrReferenceWrapper);
                }

                int identity = ParseIdentity(identityMarkers[0].Value);
                string? opaqueClass = null;
                if (classMarkers.Count == 1)
                {
                    if (classMarkers[0].Value is not AtlasLosslessJsonScalar
                        {
                            Kind: AtlasJsonScalarKind.Text,
                            StringValue: { Length: > 0 },
                        } classValue)
                    {
                        throw new AtlasSaveReadException(
                            AtlasSaveReadFailure.InvalidMarkerType);
                    }

                    opaqueClass = classValue.StringValue;
                }

                AtlasJsonExObject result = AddNode(
                    new AtlasJsonExObject(syntax, identity, opaqueClass));
                RegisterIdentity(identity, result);
                AddOrdinaryMembers(result, syntax);
                return result;
            }

            if (classMarkers.Count != 0
                || arrayMarkers.Count != 0
                || referenceMarkers.Count != 0)
            {
                throw new AtlasSaveReadException(AtlasSaveReadFailure.InvalidMarkerType);
            }

            AtlasJsonExObject plain = AddNode(
                new AtlasJsonExObject(syntax, null, null));
            AddOrdinaryMembers(plain, syntax);
            return plain;
        }

        private void AddOrdinaryMembers(
            AtlasJsonExObject destination,
            AtlasLosslessJsonObject syntax)
        {
            foreach (AtlasLosslessJsonMember member in syntax.Members)
            {
                if (member.Name is "@" or "@c" or "@a" or "@r")
                {
                    continue;
                }

                destination.MutableMembers.Add(
                    new AtlasJsonExMember(
                        member.Name,
                        member.RawNameLexeme,
                        BuildNode(member.Value)));
            }
        }

        private void RegisterIdentity(int identity, AtlasJsonExNode node)
        {
            if (identities.Count >= limits.MaximumIdentityDefinitions)
            {
                throw new AtlasSaveReadException(
                    AtlasSaveReadFailure.IdentityCountLimit);
            }

            if (!identities.TryAdd(identity, node))
            {
                throw new AtlasSaveReadException(AtlasSaveReadFailure.DuplicateIdentity);
            }
        }

        private T AddNode<T>(T node)
            where T : AtlasJsonExNode
        {
            if (materializedNodes >= limits.MaximumGraphNodes)
            {
                throw new AtlasSaveReadException(AtlasSaveReadFailure.GraphNodeLimit);
            }

            materializedNodes++;
            return node;
        }

        private static List<AtlasLosslessJsonMember> GetMembers(
            AtlasLosslessJsonObject value,
            string name) =>
            value.Members
                .Where(member => StringComparer.Ordinal.Equals(member.Name, name))
                .ToList();

        private static int ParseIdentity(AtlasLosslessJsonValue value)
        {
            if (value is not AtlasLosslessJsonScalar
                {
                    Kind: AtlasJsonScalarKind.Number,
                } scalar)
            {
                throw new AtlasSaveReadException(AtlasSaveReadFailure.InvalidMarkerType);
            }

            string token = scalar.RawLexeme;
            if (token.Length == 0
                || (token.Length > 1 && token[0] == '0')
                || token.Any(static character => character is < '0' or > '9')
                || !int.TryParse(
                    token,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int identity)
                || identity < 0)
            {
                throw new AtlasSaveReadException(AtlasSaveReadFailure.InvalidMarkerType);
            }

            return identity;
        }

        private long CountCycles(AtlasJsonExNode root)
        {
            Dictionary<AtlasJsonExNode, byte> states =
                new(ReferenceEqualityComparer.Instance);
            Stack<CycleFrame> stack = [];
            long cycles = 0;
            states.Add(root, 1);
            stack.Push(new CycleFrame(root, GetChildren(root).GetEnumerator()));
            while (stack.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CycleFrame frame = stack.Peek();
                if (!frame.Children.MoveNext())
                {
                    frame.Children.Dispose();
                    states[frame.Node] = 2;
                    stack.Pop();
                    continue;
                }

                AtlasJsonExNode child = frame.Children.Current;
                if (states.TryGetValue(child, out byte state))
                {
                    if (state == 1)
                    {
                        cycles++;
                    }

                    continue;
                }

                states.Add(child, 1);
                stack.Push(new CycleFrame(child, GetChildren(child).GetEnumerator()));
            }

            return cycles;
        }

        private static IEnumerable<AtlasJsonExNode> GetChildren(AtlasJsonExNode node) =>
            node switch
            {
                AtlasJsonExObject value => value.Members.Select(static member => member.Value),
                AtlasJsonExArray value => value.Elements,
                AtlasJsonExReference value => [value.Target],
                _ => [],
            };

        private sealed record CycleFrame(
            AtlasJsonExNode Node,
            IEnumerator<AtlasJsonExNode> Children);
    }
}
