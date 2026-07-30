using System.Globalization;
using System.Text;

namespace Hcoona.CelesphoniaModifier.Atlas;

public enum AtlasGoldMutationDisposition
{
    Unchanged,
    Changed,
}

public enum AtlasGoldMutationFailure
{
    SourceIncomplete,
    SourceDisagrees,
    InvalidSourceSpan,
    OverlappingSourceSpans,
    CandidateLimitExceeded,
    CandidateVerificationFailed,
    UnsupportedInternalState,
}

public sealed class AtlasGoldMutationException : Exception
{
    internal AtlasGoldMutationException(AtlasGoldMutationFailure failure)
        : base(GetMessage(failure))
    {
        Failure = failure;
    }

    public AtlasGoldMutationFailure Failure { get; }

    private static string GetMessage(AtlasGoldMutationFailure failure) =>
        failure switch
        {
            AtlasGoldMutationFailure.SourceIncomplete =>
                "The Gold source is incomplete.",
            AtlasGoldMutationFailure.SourceDisagrees =>
                "The Gold source candidates disagree.",
            AtlasGoldMutationFailure.InvalidSourceSpan =>
                "A Gold source span is invalid.",
            AtlasGoldMutationFailure.OverlappingSourceSpans =>
                "Gold source spans overlap.",
            AtlasGoldMutationFailure.CandidateLimitExceeded =>
                "The Gold candidate exceeds the configured limits.",
            AtlasGoldMutationFailure.CandidateVerificationFailed =>
                "The Gold candidate could not be verified.",
            _ => "The Gold mutation kernel reached an unsupported internal state.",
        };
}

public sealed class AtlasGoldMutationResult
{
    private const int CopyChunkSize = 16 * 1024;
    private readonly byte[] compressedBytes;

    private AtlasGoldMutationResult(
        AtlasGoldMutationDisposition disposition,
        byte[] compressedBytes)
    {
        Disposition = disposition;
        this.compressedBytes = compressedBytes;
    }

    public AtlasGoldMutationDisposition Disposition { get; }

    public byte[] GetCompressedBytes(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        byte[] result = new byte[compressedBytes.Length];
        for (int offset = 0; offset < compressedBytes.Length; offset += CopyChunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int length = Math.Min(CopyChunkSize, compressedBytes.Length - offset);
            compressedBytes.AsSpan(offset, length).CopyTo(result.AsSpan(offset, length));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    internal static AtlasGoldMutationResult TakeOwnership(
        AtlasGoldMutationDisposition disposition,
        byte[] compressedBytes,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AtlasGoldMutationResult result = new(disposition, compressedBytes);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
}

internal enum AtlasGoldSpanRelationship
{
    Distinct,
    Equal,
    Overlapping,
}

internal readonly struct AtlasGoldNormalizedSpan
{
    internal AtlasGoldNormalizedSpan(int start, int length)
    {
        Start = start;
        Length = length;
    }

    internal int Start { get; }

    internal int Length { get; }

    internal int End => checked(Start + Length);
}

internal readonly struct AtlasGoldNormalizedSpanSet
{
    internal AtlasGoldNormalizedSpanSet(
        AtlasGoldNormalizedSpan first,
        AtlasGoldNormalizedSpan? second)
    {
        First = first;
        Second = second;
    }

    internal AtlasGoldNormalizedSpan First { get; }

    internal AtlasGoldNormalizedSpan? Second { get; }

    internal int Count => Second.HasValue ? 2 : 1;
}

public static class AtlasGoldMutationKernel
{
    private const int CopyChunkSize = 16 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static AtlasGoldMutationResult CreateCandidate(
        AtlasSaveReadResult source,
        long value,
        AtlasSaveReaderLimits limits,
        CancellationToken cancellationToken = default)
    {
        if (source is null)
        {
            throw new ArgumentNullException(
                nameof(source),
                "The Atlas save read result is required.");
        }

        if (limits is null)
        {
            throw new ArgumentNullException(
                nameof(limits),
                "The Atlas save reader limits are required.");
        }

        limits.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        AtlasGoldInspectionResult inspection =
            AtlasGoldReadModel.Inspect(source, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        AtlasGoldMutationFailure? sourceFailure = ClassifySource(
            inspection,
            out long currentValue);
        cancellationToken.ThrowIfCancellationRequested();
        if (sourceFailure.HasValue)
        {
            throw new AtlasGoldMutationException(sourceFailure.Value);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (value == currentValue)
        {
            byte[] semanticNoOp = source.GetSemanticNoOpBytes();
            cancellationToken.ThrowIfCancellationRequested();
            return AtlasGoldMutationResult.TakeOwnership(
                AtlasGoldMutationDisposition.Unchanged,
                semanticNoOp,
                cancellationToken);
        }

        if (inspection.PartyGoldSpan is not AtlasJsonSourceSpan partySpan
            || inspection.VariableGoldSpan is not AtlasJsonSourceSpan variableSpan)
        {
            throw new AtlasGoldMutationException(
                AtlasGoldMutationFailure.UnsupportedInternalState);
        }

        ReadOnlyMemory<byte> sourceUtf8 = source.Json.Utf8Source;
        AtlasGoldNormalizedSpanSet spans = NormalizeSourceSpans(
            sourceUtf8,
            currentValue,
            partySpan,
            variableSpan,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] replacement = Encoding.ASCII.GetBytes(
            value.ToString(CultureInfo.InvariantCulture));
        cancellationToken.ThrowIfCancellationRequested();
        byte[] candidateUtf8 = ConstructCandidate(
            sourceUtf8,
            spans,
            replacement,
            cancellationToken);

        string candidateText = DecodeCandidateUtf8(
            candidateUtf8,
            limits,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] encodedCandidate;
        try
        {
            encodedCandidate = AtlasLzStringCodec.CompressToBase64(
                candidateText,
                limits,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AtlasLzStringException exception)
        {
            throw new AtlasGoldMutationException(
                MapCodecFailure(exception.Failure));
        }
        catch (OverflowException)
        {
            throw new AtlasGoldMutationException(
                AtlasGoldMutationFailure.UnsupportedInternalState);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (encodedCandidate.Length > limits.MaximumEncodedBytes)
        {
            throw new AtlasGoldMutationException(
                AtlasGoldMutationFailure.CandidateLimitExceeded);
        }

        AtlasSaveReadResult candidate;
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            candidate = AtlasSaveReader.Read(
                encodedCandidate,
                limits,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AtlasSaveReadException exception)
        {
            throw new AtlasGoldMutationException(
                MapReaderFailure(exception.Failure));
        }
        catch (OverflowException)
        {
            throw new AtlasGoldMutationException(
                AtlasGoldMutationFailure.UnsupportedInternalState);
        }

        cancellationToken.ThrowIfCancellationRequested();
        AtlasGoldMutationFailure? verificationFailure = VerifyCandidate(
            candidateUtf8,
            candidate,
            value,
            cancellationToken);
        if (verificationFailure.HasValue)
        {
            throw new AtlasGoldMutationException(verificationFailure.Value);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return AtlasGoldMutationResult.TakeOwnership(
            AtlasGoldMutationDisposition.Changed,
            encodedCandidate,
            cancellationToken);
    }

    internal static AtlasGoldMutationFailure? ClassifySource(
        AtlasGoldInspectionResult inspection,
        out long currentValue)
    {
        currentValue = default;
        AtlasGoldCandidateResult party = inspection.ReadModel.PartyGold;
        AtlasGoldCandidateResult variable = inspection.ReadModel.VariableGold;
        bool partyPresent = party.State == AtlasGoldCandidateState.Present;
        bool variablePresent = variable.State == AtlasGoldCandidateState.Present;
        if (partyPresent != party.Value.HasValue
            || variablePresent != variable.Value.HasValue
            || partyPresent != inspection.PartyGoldSpan.HasValue
            || variablePresent != inspection.VariableGoldSpan.HasValue)
        {
            return AtlasGoldMutationFailure.UnsupportedInternalState;
        }

        return inspection.ReadModel.Aggregate switch
        {
            AtlasGoldAggregateState.Incomplete when !partyPresent || !variablePresent =>
                AtlasGoldMutationFailure.SourceIncomplete,
            AtlasGoldAggregateState.Disagree
                when partyPresent
                    && variablePresent
                    && party.Value != variable.Value =>
                AtlasGoldMutationFailure.SourceDisagrees,
            AtlasGoldAggregateState.Consistent
                when partyPresent
                    && variablePresent
                    && party.Value == variable.Value =>
                SetCurrentValue(party.Value!.Value, out currentValue),
            _ => AtlasGoldMutationFailure.UnsupportedInternalState,
        };
    }

    internal static AtlasGoldNormalizedSpanSet NormalizeSourceSpans(
        ReadOnlyMemory<byte> utf8Source,
        long expectedValue,
        AtlasJsonSourceSpan first,
        AtlasJsonSourceSpan second,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AtlasGoldNormalizedSpan normalizedFirst = NormalizeSourceSpan(
            utf8Source,
            expectedValue,
            first,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        AtlasGoldNormalizedSpan normalizedSecond = NormalizeSourceSpan(
            utf8Source,
            expectedValue,
            second,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return CreateNormalizedSpanSet(
            normalizedFirst,
            normalizedSecond);
    }

    internal static AtlasGoldNormalizedSpanSet CreateNormalizedSpanSet(
        AtlasGoldNormalizedSpan first,
        AtlasGoldNormalizedSpan second)
    {
        AtlasGoldSpanRelationship relationship = ClassifySpanPair(first, second);
        if (relationship == AtlasGoldSpanRelationship.Equal)
        {
            return new AtlasGoldNormalizedSpanSet(first, null);
        }

        if (relationship == AtlasGoldSpanRelationship.Overlapping)
        {
            throw new AtlasGoldMutationException(
                AtlasGoldMutationFailure.OverlappingSourceSpans);
        }

        return first.Start < second.Start
            ? new AtlasGoldNormalizedSpanSet(first, second)
            : new AtlasGoldNormalizedSpanSet(second, first);
    }

    internal static AtlasGoldNormalizedSpan NormalizeSourceSpan(
        ReadOnlyMemory<byte> utf8Source,
        long expectedValue,
        AtlasJsonSourceSpan span,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        long end;
        try
        {
            end = checked(span.Start + span.Length);
        }
        catch (OverflowException)
        {
            throw new AtlasGoldMutationException(
                AtlasGoldMutationFailure.InvalidSourceSpan);
        }

        if (span.Start < 0
            || span.Length <= 0
            || end > utf8Source.Length
            || span.Start > int.MaxValue
            || span.Length > int.MaxValue)
        {
            throw new AtlasGoldMutationException(
                AtlasGoldMutationFailure.InvalidSourceSpan);
        }

        int start = checked((int)span.Start);
        int length = checked((int)span.Length);
        ReadOnlySpan<byte> lexeme = utf8Source.Span.Slice(start, length);
        if (!TryParseIntegerLexeme(
                lexeme,
                cancellationToken,
                out long parsedValue)
            || parsedValue != expectedValue)
        {
            throw new AtlasGoldMutationException(
                AtlasGoldMutationFailure.InvalidSourceSpan);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return new AtlasGoldNormalizedSpan(start, length);
    }

    internal static AtlasGoldSpanRelationship ClassifySpanPair(
        AtlasGoldNormalizedSpan first,
        AtlasGoldNormalizedSpan second)
    {
        if (first.Start == second.Start && first.Length == second.Length)
        {
            return AtlasGoldSpanRelationship.Equal;
        }

        return first.Start < second.End && second.Start < first.End
            ? AtlasGoldSpanRelationship.Overlapping
            : AtlasGoldSpanRelationship.Distinct;
    }

    internal static bool TryParseIntegerLexeme(
        ReadOnlySpan<byte> lexeme,
        CancellationToken cancellationToken,
        out long value)
    {
        cancellationToken.ThrowIfCancellationRequested();
        value = default;
        bool negative = lexeme.Length > 0 && lexeme[0] == (byte)'-';
        int index = negative ? 1 : 0;
        if (index == lexeme.Length)
        {
            return false;
        }

        byte firstDigit = lexeme[index];
        if (firstDigit == (byte)'0')
        {
            if (index + 1 != lexeme.Length)
            {
                return false;
            }

            value = 0;
            return true;
        }

        if (firstDigit is < (byte)'1' or > (byte)'9')
        {
            return false;
        }

        ulong maximumMagnitude = negative
            ? 9_223_372_036_854_775_808UL
            : 9_223_372_036_854_775_807UL;
        ulong magnitude = 0;
        for (; index < lexeme.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            byte character = lexeme[index];
            if (character is < (byte)'0' or > (byte)'9')
            {
                return false;
            }

            ulong digit = (uint)(character - (byte)'0');
            if (magnitude > (maximumMagnitude - digit) / 10)
            {
                return false;
            }

            magnitude = (magnitude * 10) + digit;
        }

        value = negative
            ? magnitude == 9_223_372_036_854_775_808UL
                ? long.MinValue
                : -(long)magnitude
            : (long)magnitude;
        return true;
    }

    internal static byte[] ConstructCandidate(
        ReadOnlyMemory<byte> source,
        AtlasGoldNormalizedSpanSet spans,
        ReadOnlyMemory<byte> replacement,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        int candidateLength;
        try
        {
            long length = checked(
                source.Length
                - spans.First.Length
                + replacement.Length);
            if (spans.Second is AtlasGoldNormalizedSpan second)
            {
                length = checked(length - second.Length + replacement.Length);
            }

            if (length < 0)
            {
                throw new OverflowException();
            }

            candidateLength = checked((int)length);
        }
        catch (OverflowException)
        {
            throw new AtlasGoldMutationException(
                AtlasGoldMutationFailure.UnsupportedInternalState);
        }

        byte[] candidate = new byte[candidateLength];
        int sourceOffset = 0;
        int destinationOffset = 0;
        CopyReplacement(spans.First);
        if (spans.Second is AtlasGoldNormalizedSpan secondSpan)
        {
            CopyReplacement(secondSpan);
        }

        CopyChunked(
            source.Span[sourceOffset..],
            candidate,
            ref destinationOffset,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (destinationOffset != candidate.Length)
        {
            throw new AtlasGoldMutationException(
                AtlasGoldMutationFailure.UnsupportedInternalState);
        }

        return candidate;

        void CopyReplacement(AtlasGoldNormalizedSpan span)
        {
            CopyChunked(
                source.Span[sourceOffset..span.Start],
                candidate,
                ref destinationOffset,
                cancellationToken);
            CopyChunked(
                replacement.Span,
                candidate,
                ref destinationOffset,
                cancellationToken);
            sourceOffset = span.End;
        }
    }

    internal static string DecodeCandidateUtf8(
        ReadOnlyMemory<byte> candidateUtf8,
        AtlasSaveReaderLimits limits,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string candidateText;
        try
        {
            candidateText = StrictUtf8.GetString(candidateUtf8.Span);
        }
        catch (DecoderFallbackException)
        {
            throw new AtlasGoldMutationException(
                AtlasGoldMutationFailure.CandidateVerificationFailed);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (candidateText.Length > limits.MaximumDecompressedCodeUnits)
        {
            throw new AtlasGoldMutationException(
                AtlasGoldMutationFailure.CandidateLimitExceeded);
        }

        return candidateText;
    }

    internal static AtlasGoldMutationFailure? VerifyCandidate(
        ReadOnlyMemory<byte> expectedUtf8,
        AtlasSaveReadResult candidate,
        long expectedValue,
        CancellationToken cancellationToken)
    {
        if (!BytesEqual(
                expectedUtf8,
                candidate.Json.Utf8Source,
                cancellationToken))
        {
            return AtlasGoldMutationFailure.CandidateVerificationFailed;
        }

        cancellationToken.ThrowIfCancellationRequested();
        AtlasGoldInspectionResult inspection =
            AtlasGoldReadModel.Inspect(candidate, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return VerifyInspection(inspection, expectedValue);
    }

    internal static AtlasGoldMutationFailure? VerifyInspection(
        AtlasGoldInspectionResult inspection,
        long expectedValue)
    {
        AtlasGoldMutationFailure? sourceFailure = ClassifySource(
            inspection,
            out long currentValue);
        return sourceFailure switch
        {
            null when currentValue == expectedValue => null,
            AtlasGoldMutationFailure.UnsupportedInternalState =>
                AtlasGoldMutationFailure.UnsupportedInternalState,
            _ => AtlasGoldMutationFailure.CandidateVerificationFailed,
        };
    }

    internal static bool BytesEqual(
        ReadOnlyMemory<byte> first,
        ReadOnlyMemory<byte> second,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (first.Length != second.Length)
        {
            return false;
        }

        for (int offset = 0; offset < first.Length; offset += CopyChunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int length = Math.Min(CopyChunkSize, first.Length - offset);
            if (!first.Span.Slice(offset, length).SequenceEqual(
                    second.Span.Slice(offset, length)))
            {
                return false;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return true;
    }

    internal static AtlasGoldMutationFailure MapCodecFailure(
        AtlasLzStringFailure failure) =>
        failure switch
        {
            AtlasLzStringFailure.EncodedInputLimit
                or AtlasLzStringFailure.DecompressedSizeLimit =>
                AtlasGoldMutationFailure.CandidateLimitExceeded,
            AtlasLzStringFailure.UnsupportedState =>
                AtlasGoldMutationFailure.UnsupportedInternalState,
            AtlasLzStringFailure.InvalidAlphabet
                or AtlasLzStringFailure.InvalidPadding
                or AtlasLzStringFailure.MalformedOrTruncated =>
                AtlasGoldMutationFailure.CandidateVerificationFailed,
            _ => AtlasGoldMutationFailure.UnsupportedInternalState,
        };

    internal static AtlasGoldMutationFailure MapReaderFailure(
        AtlasSaveReadFailure failure) =>
        failure switch
        {
            AtlasSaveReadFailure.EncodedInputLimit
                or AtlasSaveReadFailure.DecompressedSizeLimit
                or AtlasSaveReadFailure.JsonDepthLimit
                or AtlasSaveReadFailure.JsonTokenLimit
                or AtlasSaveReadFailure.ScalarSizeLimit
                or AtlasSaveReadFailure.GraphNodeLimit
                or AtlasSaveReadFailure.IdentityCountLimit
                or AtlasSaveReadFailure.ReferenceCountLimit =>
                AtlasGoldMutationFailure.CandidateLimitExceeded,
            AtlasSaveReadFailure.UnsupportedInternalState =>
                AtlasGoldMutationFailure.UnsupportedInternalState,
            AtlasSaveReadFailure.InvalidCompressedAlphabetOrPadding
                or AtlasSaveReadFailure.MalformedOrTruncatedCompressedInput
                or AtlasSaveReadFailure.MalformedJson
                or AtlasSaveReadFailure.DuplicateIdentity
                or AtlasSaveReadFailure.DanglingReference
                or AtlasSaveReadFailure.InvalidMarkerType
                or AtlasSaveReadFailure.InvalidArrayOrReferenceWrapper =>
                AtlasGoldMutationFailure.CandidateVerificationFailed,
            _ => AtlasGoldMutationFailure.UnsupportedInternalState,
        };

    private static AtlasGoldMutationFailure? SetCurrentValue(
        long value,
        out long currentValue)
    {
        currentValue = value;
        return null;
    }

    private static void CopyChunked(
        ReadOnlySpan<byte> source,
        Span<byte> destination,
        ref int destinationOffset,
        CancellationToken cancellationToken)
    {
        for (int offset = 0; offset < source.Length; offset += CopyChunkSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int length = Math.Min(CopyChunkSize, source.Length - offset);
            source.Slice(offset, length).CopyTo(
                destination.Slice(destinationOffset, length));
            destinationOffset += length;
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
