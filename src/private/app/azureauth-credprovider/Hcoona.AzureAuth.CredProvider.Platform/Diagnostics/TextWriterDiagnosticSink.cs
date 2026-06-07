using System.Globalization;

namespace Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

public sealed class TextWriterDiagnosticSink : IDiagnosticSink
{
    private readonly DiagnosticChannel _channel;
    private readonly DiagnosticSeverity _minimumSeverity;
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
        _minimumSeverity = minimumSeverity;
        _channel = channel;
    }

    public void Write(DiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);

        if (diagnosticEvent.Channel != _channel ||
            diagnosticEvent.Severity < _minimumSeverity)
        {
            return;
        }

        _writer.Write(diagnosticEvent.Timestamp.ToString("O", CultureInfo.InvariantCulture));
        _writer.Write(" [");
        _writer.Write(diagnosticEvent.Severity);
        _writer.Write("]");

        if (diagnosticEvent.CorrelationId is not null)
        {
            _writer.Write(" [");
            _writer.Write(diagnosticEvent.CorrelationId);
            _writer.Write("]");
        }

        _writer.Write(' ');
        _writer.Write(diagnosticEvent.Message);

        foreach (var property in diagnosticEvent.Properties)
        {
            _writer.Write(' ');
            _writer.Write(property.Key);
            _writer.Write('=');
            _writer.Write(property.Value);
        }

        _writer.WriteLine();
    }
}
