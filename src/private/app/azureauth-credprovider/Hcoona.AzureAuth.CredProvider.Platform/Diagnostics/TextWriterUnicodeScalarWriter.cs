using System.Reflection;
using System.Text;

namespace Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

internal static class TextWriterUnicodeScalarWriter
{
    private static readonly Assembly BuiltInEncodingAssembly = typeof(Encoding).Assembly;
    private static readonly Assembly? CodePagesEncodingAssembly = Type
        .GetType(
            "System.Text.CodePagesEncodingProvider, System.Text.Encoding.CodePages",
            throwOnError: false)
        ?.Assembly;

    public static void Write(
        TextWriter writer,
        string value,
        ref bool outputCommitted,
        bool trackCommit)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);

        ScalarChunkWriter chunkWriter = CreateChunkWriter(writer);
        if (chunkWriter.RequiresWellFormedUtf16Preflight)
        {
            EnsureWellFormedUtf16(value);
            chunkWriter.EnsureValueCanBeEncodedExactly(value);
        }

        if (ContainsNonBmpScalar(value))
        {
            EnsureWriterSupportsNonBmpScalarChunks(chunkWriter, writer);
        }

        for (var index = 0; index < value.Length; index++)
        {
            int chunkLength =
                char.IsHighSurrogate(value[index])
                && index + 1 < value.Length
                && char.IsLowSurrogate(value[index + 1])
                    ? 2
                    : 1;

            string chunk = new(value.AsSpan(index, chunkLength));
            chunkWriter.Write(chunk, ref outputCommitted, trackCommit);
            index += chunkLength - 1;
        }
    }

    private static bool ContainsNonBmpScalar(string value)
    {
        for (var index = 0; index + 1 < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]) && char.IsLowSurrogate(value[index + 1]))
            {
                return true;
            }
        }

        return false;
    }

    private static void EnsureWellFormedUtf16(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (!char.IsSurrogate(value[index]))
            {
                continue;
            }

            if (char.IsHighSurrogate(value[index])
                && index + 1 < value.Length
                && char.IsLowSurrogate(value[index + 1]))
            {
                index++;
                continue;
            }

            throw new EncoderFallbackException("String contains malformed UTF-16.");
        }
    }

    private static ScalarChunkWriter CreateChunkWriter(TextWriter writer)
    {
        TextWriter effectiveWriter = TextWriterSynchronization.GetSupportedWrappedWriterOrSelf(
            writer);
        if (effectiveWriter is IProgressAwareTextWriter progressAwareWriter)
        {
            return new ScalarChunkWriter(
                effectiveWriter,
                progressAwareWriter,
                stringWriter: null,
                streamWriter: null,
                requiresWellFormedUtf16Preflight:
                    effectiveWriter is ITrustedWholeValueUtf16PreflightTextWriter);
        }

        if (effectiveWriter is StreamWriter streamWriter)
        {
            return CreateStreamWriterChunkWriter(effectiveWriter, streamWriter);
        }

        if (
            effectiveWriter is StringWriter stringWriter
            && UsesBuiltInStringWriterWriteImplementation(effectiveWriter.GetType())
        )
        {
            return new ScalarChunkWriter(
                effectiveWriter,
                progressAwareWriter: null,
                stringWriter,
                streamWriter: null);
        }

        return new ScalarChunkWriter(
            effectiveWriter,
            progressAwareWriter: null,
            stringWriter: null,
            streamWriter: null);
    }

    private static void EnsureWriterSupportsNonBmpScalarChunks(
        ScalarChunkWriter chunkWriter,
        TextWriter writer)
    {
        if (chunkWriter.SupportsNonBmpStringChunks)
        {
            return;
        }

        throw new NotSupportedException(
            $"TextWriter type '{writer.GetType().FullName}' must use the built-in "
                + $"{nameof(StreamWriter)} or {nameof(StringWriter)} "
                + $"{nameof(TextWriter.Write)}(string) implementation "
                + $"or implement {nameof(IProgressAwareTextWriter)} "
                + "to safely emit non-BMP text.");
    }

    private static bool UsesBuiltInStringWriterWriteImplementation(Type writerType)
    {
        MethodInfo? method = writerType.GetMethod(nameof(TextWriter.Write), [typeof(string)]);
        return method?.DeclaringType == typeof(StringWriter);
    }

    private static bool IsExactBuiltInStreamWriter(Type writerType)
    {
        return writerType == typeof(StreamWriter);
    }

    private static ScalarChunkWriter CreateStreamWriterChunkWriter(
        TextWriter effectiveWriter,
        StreamWriter streamWriter)
    {
        if (!IsExactBuiltInStreamWriter(effectiveWriter.GetType()))
        {
            throw new NotSupportedException(
                $"TextWriter type '{effectiveWriter.GetType().FullName}' is unsupported because "
                    + $"only the exact built-in {nameof(StreamWriter)} type can safely cross the "
                    + "encoded writer trust boundary without an explicit progress-aware opt-in.");
        }

        if (!IsTrustedBuiltInEncoding(streamWriter.Encoding))
        {
            throw new NotSupportedException(
                $"Exact {nameof(StreamWriter)} instances that use {nameof(Encoding)} type "
                    + $"'{streamWriter.Encoding.GetType().FullName}' are unsupported because "
                    + "strict preflight is only trusted for built-in encodings.");
        }

        if (EncodingEmitsPreamble(streamWriter.Encoding))
        {
            throw new NotSupportedException(
                $"Exact {nameof(StreamWriter)} instances that use {nameof(Encoding)} type "
                    + $"'{streamWriter.Encoding.GetType().FullName}' are unsupported because "
                    + "encodings that emit a preamble cannot safely cross the shared-stream "
                    + "writer trust boundary.");
        }

        return new ScalarChunkWriter(
            effectiveWriter,
            progressAwareWriter: null,
            stringWriter: null,
            streamWriter);
    }

    private static bool IsTrustedBuiltInEncoding(Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);

        Assembly encodingAssembly = encoding.GetType().Assembly;
        return encodingAssembly == BuiltInEncodingAssembly
            || (CodePagesEncodingAssembly is not null
                && encodingAssembly == CodePagesEncodingAssembly);
    }

    private static bool EncodingEmitsPreamble(Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);

        return encoding.GetPreamble().Length != 0;
    }

    private static Encoding CreateStrictEncoding(Encoding encoding)
    {
        Encoding strictEncoding = (Encoding)encoding.Clone();
        strictEncoding.EncoderFallback = EncoderFallback.ExceptionFallback;
        return strictEncoding;
    }

    private readonly struct ScalarChunkWriter
    {
        private readonly TextWriter _writer;
        private readonly IProgressAwareTextWriter? _progressAwareWriter;
        private readonly bool _requiresWellFormedUtf16Preflight;
        private readonly StringWriter? _stringWriter;
        private readonly StreamWriter? _streamWriter;

        public ScalarChunkWriter(
            TextWriter writer,
            IProgressAwareTextWriter? progressAwareWriter,
            StringWriter? stringWriter,
            StreamWriter? streamWriter,
            bool requiresWellFormedUtf16Preflight = false)
        {
            _writer = writer;
            _progressAwareWriter = progressAwareWriter;
            _requiresWellFormedUtf16Preflight = requiresWellFormedUtf16Preflight;
            _stringWriter = stringWriter;
            _streamWriter = streamWriter;
        }

        public bool SupportsNonBmpStringChunks =>
            _progressAwareWriter is not null
            || _stringWriter is not null
            || _streamWriter is not null;

        public bool RequiresWellFormedUtf16Preflight =>
            _requiresWellFormedUtf16Preflight
            || _streamWriter is not null
            || _stringWriter is not null;

        public void EnsureValueCanBeEncodedExactly(string value)
        {
            if (_streamWriter is null || value.Length == 0)
            {
                return;
            }

            _ = CreateStrictEncoding(_streamWriter.Encoding).GetByteCount(value);
        }

        public void Write(string chunk, ref bool outputCommitted, bool trackCommit)
        {
            if (_progressAwareWriter is not null)
            {
                WriteWithReportedProgress(chunk.AsSpan(), ref outputCommitted);
                return;
            }

            if (_stringWriter is not null)
            {
                WriteWithStringBuilderProgress(chunk, ref outputCommitted);
                return;
            }

            if (_streamWriter is not null)
            {
                WriteWithOrdinaryStreamWriterProgress(chunk, ref outputCommitted, trackCommit);
                return;
            }

            if (trackCommit && chunk.Length != 0)
            {
                // Plain TextWriter.Write(string) provides no reliable progress signal.
                // In tracked mode, conservatively treat a non-empty chunk as committed
                // before the call so append-then-throw implementations are not
                // misclassified as zero-byte failures.
                outputCommitted = true;
            }

            _writer.Write(chunk);
            outputCommitted = true;
        }

        private void WriteWithReportedProgress(
            ReadOnlySpan<char> value,
            ref bool outputCommitted)
        {
            var charsWritten = 0;
            try
            {
                _progressAwareWriter!.WriteWithProgress(value, ref charsWritten);
            }
            catch
            {
                outputCommitted |= charsWritten != 0;
                throw;
            }

            outputCommitted = true;
        }

        private void WriteWithOrdinaryStreamWriterProgress(
            string value,
            ref bool outputCommitted,
            bool trackCommit)
        {
            if (trackCommit && value.Length != 0)
            {
                // Built-in StreamWriter can still partially commit bytes once the
                // preflighted chunk reaches the underlying stream or a subsequent
                // flush starts encoding buffered characters.
                outputCommitted = true;
            }

            _streamWriter!.Write(value);
            outputCommitted = true;
        }

        private void WriteWithStringBuilderProgress(string value, ref bool outputCommitted)
        {
            int initialLength = _stringWriter!.GetStringBuilder().Length;
            try
            {
                _stringWriter.Write(value);
            }
            catch
            {
                outputCommitted |= _stringWriter.GetStringBuilder().Length != initialLength;
                throw;
            }

            outputCommitted = true;
        }
    }
}

internal interface IProgressAwareTextWriter
{
    void WriteWithProgress(ReadOnlySpan<char> value, ref int charsWritten);
}

// Trusted progress-aware writers can opt in to whole-value UTF-16 validation before
// scalar chunking so trailing malformed surrogates cannot partially commit output.
internal interface ITrustedWholeValueUtf16PreflightTextWriter
{
}
