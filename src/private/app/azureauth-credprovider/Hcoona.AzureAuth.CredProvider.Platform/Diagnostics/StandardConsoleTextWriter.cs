using System.Text;

namespace Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

internal sealed class StandardConsoleTextWriter
    : TextWriter,
        IProgressAwareTextWriter,
        IFlushRequiredTextWriter,
        ITrustedWholeValueUtf16PreflightTextWriter,
        ITextWriterSyncRootProvider
{
    private static readonly UTF8Encoding TrustedConsoleEncoding = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private static readonly object StandardOutputSyncRoot = new();
    private static readonly object StandardErrorSyncRoot = new();

    private readonly Stream _stream;
    private readonly Encoding _encoding;
    private readonly object _syncRoot;

    internal StandardConsoleTextWriter(Stream stream, Encoding encoding, string newLine)
        : this(
            stream,
            ValidateTrustedConsoleEncoding(encoding),
            newLine,
            TextWriterSynchronization.GetStreamSyncRoot(stream))
    {
    }

    private StandardConsoleTextWriter(
        Stream stream,
        Encoding encoding,
        string newLine,
        object syncRoot)
    {
        _stream = stream;
        _encoding = encoding;
        _syncRoot = syncRoot;
        NewLine = newLine;
    }

    public static StandardConsoleTextWriter StandardOutput() =>
        new(
            Console.OpenStandardOutput(),
            TrustedConsoleEncoding,
            Environment.NewLine,
            StandardOutputSyncRoot);

    public static StandardConsoleTextWriter StandardError() =>
        new(
            Console.OpenStandardError(),
            TrustedConsoleEncoding,
            Environment.NewLine,
            StandardErrorSyncRoot);

    private static Encoding ValidateTrustedConsoleEncoding(Encoding encoding)
    {
        ArgumentNullException.ThrowIfNull(encoding);

        if (encoding.GetType() == typeof(UTF8Encoding)
            && encoding.GetPreamble().Length == 0
            && encoding.EncoderFallback is EncoderExceptionFallback
            && encoding.DecoderFallback is DecoderExceptionFallback)
        {
            return encoding;
        }

        throw new ArgumentException(
            $"{nameof(StandardConsoleTextWriter)} only supports exact strict no-BOM UTF-8 "
                + "because its trusted progress-aware write path relies on whole-value "
                + "UTF-16 preflight.",
            nameof(encoding));
    }

    public override Encoding Encoding => _encoding;

    object ITextWriterSyncRootProvider.SyncRoot => _syncRoot;

    public override void Flush()
    {
        lock (_syncRoot)
        {
            _stream.Flush();
        }
    }

    public override void Write(char value)
    {
        Span<char> buffer = stackalloc char[1];
        buffer[0] = value;
        WriteCore(buffer);
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        WriteCore(value.AsSpan());
    }

    public override void Write(ReadOnlySpan<char> buffer)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        WriteCore(buffer);
    }

    public void WriteWithProgress(ReadOnlySpan<char> value, ref int charsWritten)
    {
        if (value.IsEmpty)
        {
            return;
        }

        byte[] encodedBytes = Encode(value);
        try
        {
            WriteEncodedBytes(encodedBytes);
            charsWritten += value.Length;
        }
        catch
        {
            // Stream writes can partially commit bytes before throwing. Conservatively
            // report the attempted chunk as committed so higher-level fallback logic
            // does not misclassify the failure as zero-byte output.
            charsWritten += value.Length;
            throw;
        }
    }

    private void WriteCore(ReadOnlySpan<char> value)
    {
        byte[] encodedBytes = Encode(value);
        WriteEncodedBytes(encodedBytes);
    }

    private byte[] Encode(ReadOnlySpan<char> value)
    {
        string text = new(value);
        return _encoding.GetBytes(text);
    }

    private void WriteEncodedBytes(byte[] encodedBytes)
    {
        lock (_syncRoot)
        {
            _stream.Write(encodedBytes, 0, encodedBytes.Length);
        }
    }
}
