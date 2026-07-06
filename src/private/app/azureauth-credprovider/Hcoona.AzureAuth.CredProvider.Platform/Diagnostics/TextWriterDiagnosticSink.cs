using System.Globalization;
using System.Text;

namespace Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

public sealed class TextWriterDiagnosticSink : ICommitTrackingDiagnosticSink
{
    private readonly DiagnosticChannel _channel;
    private readonly DiagnosticSeverity _minimumSeverity;
    private readonly object _syncRoot;
    private readonly TextWriter _writer;

    public TextWriterDiagnosticSink(
        TextWriter writer,
        DiagnosticSeverity minimumSeverity = DiagnosticSeverity.Information,
        DiagnosticChannel channel = DiagnosticChannel.Diagnostic)
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (channel == DiagnosticChannel.ProtocolStdout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channel),
                channel,
                "Protocol stdout must not be routed to diagnostic text sinks.");
        }

        _writer = writer;
        _syncRoot = TextWriterSynchronization.GetWriterSyncRoot(writer);
        _minimumSeverity = minimumSeverity;
        _channel = channel;
    }

    public void Write(DiagnosticEvent diagnosticEvent)
    {
        _ = WriteCore(diagnosticEvent, trackCommit: false);
    }

    internal bool WriteWithCommitTracking(DiagnosticEvent diagnosticEvent)
    {
        return WriteCore(diagnosticEvent, trackCommit: true);
    }

    bool ICommitTrackingDiagnosticSink.WriteWithCommitTracking(DiagnosticEvent diagnosticEvent)
    {
        return WriteWithCommitTracking(diagnosticEvent);
    }

    private bool WriteCore(DiagnosticEvent diagnosticEvent, bool trackCommit)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);

        using (TextWriterSynchronization.AcquireWriteLock(_writer, _syncRoot))
        {
            if (diagnosticEvent.Channel != _channel ||
                diagnosticEvent.Severity < _minimumSeverity)
            {
                return false;
            }

            string line = FormatDiagnosticEvent(diagnosticEvent);
            var outputCommitted = false;
            try
            {
                WriteLine(line, ref outputCommitted, trackCommit);
                TextWriterSynchronization.FlushUnderSharedLockIfNeeded(_writer);
                return outputCommitted;
            }
            catch (Exception ex) when (trackCommit)
            {
                throw new DiagnosticWriteException(outputCommitted, ex);
            }
        }
    }

    private static string FormatDiagnosticEvent(DiagnosticEvent diagnosticEvent)
    {
        var builder = new StringBuilder();
        builder.Append(diagnosticEvent.Timestamp.ToString("O", CultureInfo.InvariantCulture));
        builder.Append(" [");
        builder.Append(diagnosticEvent.Severity);
        builder.Append(']');

        if (diagnosticEvent.CorrelationId is not null)
        {
            builder.Append(" [");
            builder.Append(diagnosticEvent.CorrelationId);
            builder.Append(']');
        }

        builder.Append(' ');
        builder.Append(diagnosticEvent.Message);

        foreach (KeyValuePair<string, string?> property in diagnosticEvent.Properties)
        {
            builder.Append(' ');
            builder.Append(property.Key);
            builder.Append('=');
            builder.Append(property.Value);
        }

        return builder.ToString();
    }

    private void WriteLine(string line, ref bool outputCommitted, bool trackCommit)
    {
        string newLine = _writer.NewLine;
        // Keep the diagnostic line and the configured newline in one preflightable write so
        // exact StreamWriter instances cannot buffer the line before an invalid newline fails.
        string output = string.IsNullOrEmpty(newLine) ? line : string.Concat(line, newLine);
        TextWriterUnicodeScalarWriter.Write(
            _writer,
            output,
            ref outputCommitted,
            trackCommit: trackCommit);
    }
}
