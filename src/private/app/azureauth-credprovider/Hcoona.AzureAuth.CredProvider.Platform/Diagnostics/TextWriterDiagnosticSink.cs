using System.Globalization;
using System.Text;

namespace Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

public sealed class TextWriterDiagnosticSink : IDiagnosticSink
{
    private readonly DiagnosticChannel _channel;
    private readonly DiagnosticSeverity _minimumSeverity;
    private readonly TextWriter _writer;

    public TextWriterDiagnosticSink(
        TextWriter writer,
        DiagnosticSeverity minimumSeverity = DiagnosticSeverity.Information,
        DiagnosticChannel channel = DiagnosticChannel.Diagnostic
    )
    {
        ArgumentNullException.ThrowIfNull(writer);
        if (channel == DiagnosticChannel.ProtocolStdout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channel),
                channel,
                "Protocol stdout cannot be used as a diagnostic sink."
            );
        }

        _writer = TextWriter.Synchronized(writer);
        _minimumSeverity = minimumSeverity;
        _channel = channel;
    }

    public void Write(DiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);
        if (diagnosticEvent.Channel != _channel || diagnosticEvent.Severity < _minimumSeverity)
        {
            return;
        }

        _writer.WriteLine(FormatDiagnosticEvent(diagnosticEvent));
        _writer.Flush();
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
}
