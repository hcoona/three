using System.Buffers;
using System.Text;

namespace Hcoona.CelesphoniaModifier.Atlas;

public enum AtlasLzStringFailure
{
    InvalidAlphabet,
    InvalidPadding,
    MalformedOrTruncated,
    EncodedInputLimit,
    DecompressedSizeLimit,
    UnsupportedState,
}

public sealed class AtlasLzStringException : Exception
{
    public AtlasLzStringException(AtlasLzStringFailure failure)
        : base(GetMessage(failure))
    {
        Failure = failure;
    }

    public AtlasLzStringFailure Failure { get; }

    private static string GetMessage(AtlasLzStringFailure failure) =>
        failure switch
        {
            AtlasLzStringFailure.InvalidAlphabet =>
                "The compressed input has an invalid alphabet.",
            AtlasLzStringFailure.InvalidPadding =>
                "The compressed input has invalid padding.",
            AtlasLzStringFailure.MalformedOrTruncated =>
                "The compressed input is malformed or truncated.",
            AtlasLzStringFailure.EncodedInputLimit =>
                "The compressed input exceeds its size limit.",
            AtlasLzStringFailure.DecompressedSizeLimit =>
                "The decompressed text exceeds its size limit.",
            _ => "The compressed input reached an unsupported state.",
        };
}

public static class AtlasLzStringCodec
{
    private const string Alphabet =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    public static byte[] CompressToBase64(
        string value,
        AtlasSaveReaderLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        cancellationToken.ThrowIfCancellationRequested();
        AtlasSaveReaderLimits effectiveLimits = limits ?? AtlasSaveReaderLimits.Default;
        if (value.Length > effectiveLimits.MaximumDecompressedCodeUnits)
        {
            throw new AtlasLzStringException(AtlasLzStringFailure.DecompressedSizeLimit);
        }

        BitWordWriter writer = new(
            effectiveLimits.MaximumEncodedBytes,
            cancellationToken);
        Dictionary<char, int> characterCodes = [];
        Dictionary<SequenceKey, int> sequenceCodes = [];
        Dictionary<int, char> pendingCharacters = [];
        int wCode = -1;
        int enlargeIn = 2;
        int dictionarySize = 3;
        int numberOfBits = 2;

        for (int index = 0; index < value.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            char character = value[index];
            if (!characterCodes.TryGetValue(character, out int characterCode))
            {
                characterCode = dictionarySize++;
                characterCodes.Add(character, characterCode);
                pendingCharacters.Add(characterCode, character);
            }

            if (wCode < 0)
            {
                wCode = characterCode;
                continue;
            }

            SequenceKey candidate = new(wCode, character);
            if (sequenceCodes.TryGetValue(candidate, out int candidateCode))
            {
                wCode = candidateCode;
                continue;
            }

            WriteDictionaryValue(wCode);
            sequenceCodes.Add(candidate, dictionarySize++);
            wCode = characterCode;
        }

        if (wCode >= 0)
        {
            WriteDictionaryValue(wCode);
        }

        writer.WriteBits(2, numberOfBits);
        byte[] encoded = writer.Complete();
        if (encoded.Length > effectiveLimits.MaximumEncodedBytes)
        {
            throw new AtlasLzStringException(AtlasLzStringFailure.EncodedInputLimit);
        }

        return encoded;

        void WriteDictionaryValue(int code)
        {
            if (pendingCharacters.Remove(code, out char character))
            {
                if (character < 256)
                {
                    writer.WriteBits(0, numberOfBits);
                    writer.WriteBits(character, 8);
                }
                else
                {
                    writer.WriteBits(1, numberOfBits);
                    writer.WriteBits(character, 16);
                }

                if (--enlargeIn == 0)
                {
                    enlargeIn = 1 << numberOfBits;
                    numberOfBits++;
                }
            }
            else
            {
                writer.WriteBits(code, numberOfBits);
            }

            if (--enlargeIn == 0)
            {
                enlargeIn = 1 << numberOfBits;
                numberOfBits++;
            }
        }
    }

    public static string DecompressFromBase64(
        ReadOnlySpan<byte> encoded,
        AtlasSaveReaderLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        AtlasSaveReaderLimits effectiveLimits = limits ?? AtlasSaveReaderLimits.Default;
        if (encoded.Length > effectiveLimits.MaximumEncodedBytes)
        {
            throw new AtlasLzStringException(AtlasLzStringFailure.EncodedInputLimit);
        }

        int payloadLength = ValidateGrammar(encoded, cancellationToken);
        BitCharacterReader reader = new(encoded, payloadLength, 6, cancellationToken);
        List<DecompressionEntry> dictionary = [default, default, default];
        int firstCode = reader.ReadBits(2);
        if (firstCode == 2)
        {
            return ValidateCanonical(
                encoded,
                string.Empty,
                effectiveLimits,
                cancellationToken);
        }

        char firstCharacter = firstCode switch
        {
            0 => (char)reader.ReadBits(8),
            1 => (char)reader.ReadBits(16),
            _ => throw new AtlasLzStringException(
                AtlasLzStringFailure.MalformedOrTruncated),
        };
        dictionary.Add(DecompressionEntry.CreateLiteral(firstCharacter));
        StringBuilder output = new();
        output.Append(firstCharacter);
        int wCode = 3;
        int enlargeIn = 4;
        int dictionarySize = 4;
        int numberOfBits = 3;
        char[]? scratch = null;
        string result;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int code = reader.ReadBits(numberOfBits);
                if (code == 0 || code == 1)
                {
                    char literal = (char)reader.ReadBits(code == 0 ? 8 : 16);
                    EnsureDictionaryPosition(dictionary, dictionarySize);
                    dictionary.Add(DecompressionEntry.CreateLiteral(literal));
                    code = dictionarySize++;
                    enlargeIn--;
                }
                else if (code == 2)
                {
                    result = output.ToString();
                    break;
                }

                if (enlargeIn == 0)
                {
                    enlargeIn = 1 << numberOfBits;
                    numberOfBits++;
                }

                bool isNextDictionaryEntry = code == dictionarySize;
                int entryLength;
                char entryFirstCharacter;
                if (code >= 3
                    && code < dictionarySize
                    && code < dictionary.Count
                    && dictionary[code].Length > 0)
                {
                    DecompressionEntry entry = dictionary[code];
                    entryLength = entry.Length;
                    entryFirstCharacter = entry.FirstCharacter;
                }
                else if (isNextDictionaryEntry
                    && wCode >= 3
                    && wCode < dictionary.Count)
                {
                    DecompressionEntry previous = dictionary[wCode];
                    entryLength = checked(previous.Length + 1);
                    entryFirstCharacter = previous.FirstCharacter;
                }
                else
                {
                    throw new AtlasLzStringException(
                        AtlasLzStringFailure.MalformedOrTruncated);
                }

                if (output.Length
                    > effectiveLimits.MaximumDecompressedCodeUnits - entryLength)
                {
                    throw new AtlasLzStringException(
                        AtlasLzStringFailure.DecompressedSizeLimit);
                }

                AppendEntry(wCode, isNextDictionaryEntry, code, entryFirstCharacter);
                EnsureDictionaryPosition(dictionary, dictionarySize);
                int addedCode = dictionarySize++;
                dictionary.Add(
                    DecompressionEntry.CreateSequence(
                        wCode,
                        entryFirstCharacter,
                        dictionary[wCode]));
                enlargeIn--;
                wCode = isNextDictionaryEntry ? addedCode : code;
                if (enlargeIn == 0)
                {
                    enlargeIn = 1 << numberOfBits;
                    numberOfBits++;
                }
            }
        }
        finally
        {
            if (scratch is not null)
            {
                ArrayPool<char>.Shared.Return(scratch);
            }
        }

        return ValidateCanonical(
            encoded,
            result,
            effectiveLimits,
            cancellationToken);

        void AppendEntry(
            int previousCode,
            bool isSpecialEntry,
            int entryCode,
            char specialLastCharacter)
        {
            if (isSpecialEntry)
            {
                AppendDictionaryEntry(previousCode);
                output.Append(specialLastCharacter);
            }
            else
            {
                AppendDictionaryEntry(entryCode);
            }
        }

        void AppendDictionaryEntry(int code)
        {
            DecompressionEntry entry = dictionary[code];
            if (entry.Length == 1)
            {
                output.Append(entry.Character);
                return;
            }

            if (scratch is null || scratch.Length < entry.Length)
            {
                char[] replacement = ArrayPool<char>.Shared.Rent(entry.Length);
                if (scratch is not null)
                {
                    ArrayPool<char>.Shared.Return(scratch);
                }

                scratch = replacement;
            }

            int position = entry.Length;
            int currentCode = code;
            while (currentCode >= 3)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DecompressionEntry current = dictionary[currentCode];
                scratch[--position] = current.Character;
                currentCode = current.PrefixCode;
            }

            if (position != 0)
            {
                throw new AtlasLzStringException(AtlasLzStringFailure.UnsupportedState);
            }

            output.Append(scratch, 0, entry.Length);
        }
    }

    public static async ValueTask<string> DecompressFromBase64Async(
        Stream source,
        AtlasSaveReaderLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
        {
            throw new NotSupportedException("The source stream does not support reading.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        AtlasSaveReaderLimits effectiveLimits = limits ?? AtlasSaveReaderLimits.Default;
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
                throw new AtlasLzStringException(
                    AtlasLzStringFailure.EncodedInputLimit);
            }

            bytes.Write(buffer, 0, read);
        }

        return DecompressFromBase64(
            bytes.GetBuffer().AsSpan(0, checked((int)bytes.Length)),
            effectiveLimits,
            cancellationToken);
    }

    private static int ValidateGrammar(
        ReadOnlySpan<byte> encoded,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (encoded.Length == 0 || encoded.Length % 4 != 0)
        {
            throw new AtlasLzStringException(AtlasLzStringFailure.InvalidPadding);
        }

        int padding = 0;
        for (int index = encoded.Length - 1;
             index >= 0 && encoded[index] == (byte)'=';
             index--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            padding++;
        }

        if (padding > 3)
        {
            throw new AtlasLzStringException(AtlasLzStringFailure.InvalidPadding);
        }

        int payloadLength = encoded.Length - padding;
        int expectedPadding = (4 - (payloadLength % 4)) % 4;
        if (padding != expectedPadding)
        {
            throw new AtlasLzStringException(AtlasLzStringFailure.InvalidPadding);
        }

        for (int index = 0; index < payloadLength; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (encoded[index] == (byte)'=')
            {
                throw new AtlasLzStringException(AtlasLzStringFailure.InvalidPadding);
            }

            _ = DecodeAlphabet(encoded[index]);
        }

        for (int index = payloadLength; index < encoded.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (encoded[index] != (byte)'=')
            {
                throw new AtlasLzStringException(AtlasLzStringFailure.InvalidPadding);
            }
        }

        return payloadLength;
    }

    private static string ValidateCanonical(
        ReadOnlySpan<byte> encoded,
        string result,
        AtlasSaveReaderLimits limits,
        CancellationToken cancellationToken)
    {
        byte[] canonical = CompressToBase64(result, limits, cancellationToken);
        if (!encoded.SequenceEqual(canonical))
        {
            throw new AtlasLzStringException(
                AtlasLzStringFailure.MalformedOrTruncated);
        }

        return result;
    }

    private static void EnsureDictionaryPosition(
        List<DecompressionEntry> dictionary,
        int expectedCode)
    {
        if (dictionary.Count != expectedCode)
        {
            throw new AtlasLzStringException(AtlasLzStringFailure.UnsupportedState);
        }
    }

    private static int DecodeAlphabet(byte value)
    {
        if (value is >= (byte)'A' and <= (byte)'Z')
        {
            return value - 'A';
        }

        if (value is >= (byte)'a' and <= (byte)'z')
        {
            return value - 'a' + 26;
        }

        if (value is >= (byte)'0' and <= (byte)'9')
        {
            return value - '0' + 52;
        }

        return value switch
        {
            (byte)'+' => 62,
            (byte)'/' => 63,
            _ => throw new AtlasLzStringException(
                AtlasLzStringFailure.InvalidAlphabet),
        };
    }

    private readonly record struct SequenceKey(int PrefixCode, char Character);

    private readonly record struct DecompressionEntry(
        int PrefixCode,
        char Character,
        char FirstCharacter,
        int Length)
    {
        public static DecompressionEntry CreateLiteral(char character) =>
            new(-1, character, character, 1);

        public static DecompressionEntry CreateSequence(
            int prefixCode,
            char character,
            DecompressionEntry prefix) =>
            new(
                prefixCode,
                character,
                prefix.FirstCharacter,
                checked(prefix.Length + 1));
    }

    private sealed class BitWordWriter(
        int maximumEncodedBytes,
        CancellationToken cancellationToken)
    {
        private readonly ArrayBufferWriter<byte> output = new();
        private int value;
        private int position;

        public void WriteBits(int bits, int count)
        {
            for (int index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                value = (value << 1) | (bits & 1);
                if (position == 15)
                {
                    WriteWord();
                    position = 0;
                    value = 0;
                }
                else
                {
                    position++;
                }

                bits >>= 1;
            }
        }

        public byte[] Complete()
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                value <<= 1;
                if (position == 15)
                {
                    WriteWord();
                    cancellationToken.ThrowIfCancellationRequested();
                    string encoded = Convert.ToBase64String(output.WrittenSpan);
                    cancellationToken.ThrowIfCancellationRequested();
                    return Encoding.ASCII.GetBytes(encoded);
                }

                position++;
            }
        }

        private void WriteWord()
        {
            int requiredBytes = checked(output.WrittenCount + 2);
            long encodedLength = ((requiredBytes + 2L) / 3L) * 4L;
            if (encodedLength > maximumEncodedBytes)
            {
                throw new AtlasLzStringException(
                    AtlasLzStringFailure.EncodedInputLimit);
            }

            Span<byte> destination = output.GetSpan(2);
            destination[0] = (byte)(value >> 8);
            destination[1] = (byte)value;
            output.Advance(2);
        }
    }

    private ref struct BitCharacterReader
    {
        private readonly ReadOnlySpan<byte> encoded;
        private readonly int payloadLength;
        private readonly int bitsPerCharacter;
        private readonly CancellationToken cancellationToken;
        private int value;
        private int mask;
        private int index;

        public BitCharacterReader(
            ReadOnlySpan<byte> encoded,
            int payloadLength,
            int bitsPerCharacter,
            CancellationToken cancellationToken)
        {
            this.encoded = encoded;
            this.payloadLength = payloadLength;
            this.bitsPerCharacter = bitsPerCharacter;
            this.cancellationToken = cancellationToken;
            value = payloadLength == 0 ? 0 : DecodeAlphabet(encoded[0]);
            mask = 1 << (bitsPerCharacter - 1);
            index = 1;
        }

        public int ReadBits(int count)
        {
            int result = 0;
            int power = 1;
            for (int bit = 0; bit < count; bit++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (payloadLength == 0 || index > payloadLength)
                {
                    throw new AtlasLzStringException(
                        AtlasLzStringFailure.MalformedOrTruncated);
                }

                bool set = (value & mask) != 0;
                mask >>= 1;
                if (mask == 0)
                {
                    mask = 1 << (bitsPerCharacter - 1);
                    if (index < payloadLength)
                    {
                        value = DecodeAlphabet(encoded[index]);
                    }

                    index++;
                }

                if (set)
                {
                    result |= power;
                }

                power <<= 1;
            }

            return result;
        }
    }
}
