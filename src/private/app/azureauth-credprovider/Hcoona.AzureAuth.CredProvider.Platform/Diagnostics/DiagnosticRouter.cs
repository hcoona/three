using Hcoona.AzureAuth.CredProvider.Platform.Redaction;

namespace Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

public sealed class DiagnosticRouter
{
    private readonly IDiagnosticSink[] _diagnosticSinks;
    private readonly IDiagnosticSink[] _humanStdoutSinks;
    private readonly SecretRedactor _redactor;

    public DiagnosticRouter(
        IEnumerable<IDiagnosticSink> diagnosticSinks,
        SecretRedactor redactor,
        IEnumerable<IDiagnosticSink>? humanStdoutSinks = null)
    {
        ArgumentNullException.ThrowIfNull(diagnosticSinks);
        ArgumentNullException.ThrowIfNull(redactor);

        _diagnosticSinks = CopySinks(diagnosticSinks, nameof(diagnosticSinks));
        _humanStdoutSinks = humanStdoutSinks is null
            ? []
            : CopySinks(humanStdoutSinks, nameof(humanStdoutSinks));
        _redactor = redactor;
    }

    public void Route(DiagnosticEvent diagnosticEvent)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);

        var sinks = diagnosticEvent.Channel switch
        {
            DiagnosticChannel.Diagnostic => _diagnosticSinks,
            DiagnosticChannel.HumanStdout => _humanStdoutSinks,
            DiagnosticChannel.ProtocolStdout => Array.Empty<IDiagnosticSink>(),
            _ => Array.Empty<IDiagnosticSink>(),
        };

        if (sinks.Length == 0)
        {
            return;
        }

        var redactedEvent = Redact(diagnosticEvent);
        foreach (var sink in sinks)
        {
            sink.Write(redactedEvent);
        }
    }

    private static IDiagnosticSink[] CopySinks(IEnumerable<IDiagnosticSink> sinks, string paramName)
    {
        var copiedSinks = sinks.ToArray();
        if (Array.Exists(copiedSinks, static sink => sink is null))
        {
            throw new ArgumentException(
                "Diagnostic sinks must not contain null values.",
                paramName);
        }

        return copiedSinks;
    }

    private DiagnosticEvent Redact(DiagnosticEvent diagnosticEvent)
    {
        var message = _redactor.Redact(diagnosticEvent.Message) ?? string.Empty;
        var properties = RedactProperties(diagnosticEvent.Properties);

        if (string.Equals(message, diagnosticEvent.Message, StringComparison.Ordinal) &&
            ReferenceEquals(properties, diagnosticEvent.Properties))
        {
            return diagnosticEvent;
        }

        return new DiagnosticEvent(
            diagnosticEvent.Severity,
            diagnosticEvent.Channel,
            message,
            diagnosticEvent.CorrelationId,
            properties,
            diagnosticEvent.Timestamp);
    }

    private IReadOnlyDictionary<string, string?> RedactProperties(
        IReadOnlyDictionary<string, string?> properties)
    {
        if (properties.Count == 0)
        {
            return properties;
        }

        var redacted = new Dictionary<string, string?>(properties.Count);
        var changed = false;
        foreach (var property in properties)
        {
            var redactedKey = _redactor.Redact(property.Key) ?? string.Empty;
            var redactedValue = _redactor.Redact(property.Value);

            changed |=
                !string.Equals(redactedKey, property.Key, StringComparison.Ordinal) ||
                !string.Equals(redactedValue, property.Value, StringComparison.Ordinal);
            redacted[redactedKey] = redactedValue;
        }

        return changed ? redacted : properties;
    }
}
