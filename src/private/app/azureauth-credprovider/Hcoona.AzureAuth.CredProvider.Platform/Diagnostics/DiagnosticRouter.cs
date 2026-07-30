using Hcoona.AzureAuth.CredProvider.Platform.Redaction;

namespace Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

public sealed class DiagnosticRouter
{
    private const int MaxPropertyCount = 16;

    private readonly IDiagnosticSink[] _diagnosticSinks;
    private readonly IDiagnosticSink[] _humanStdoutSinks;
    private readonly SecretRedactor _redactor;

    public DiagnosticRouter(
        IEnumerable<IDiagnosticSink> diagnosticSinks,
        SecretRedactor redactor,
        IEnumerable<IDiagnosticSink>? humanStdoutSinks = null
    )
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
        IDiagnosticSink[] sinks = diagnosticEvent.Channel switch
        {
            DiagnosticChannel.Diagnostic => _diagnosticSinks,
            DiagnosticChannel.HumanStdout => _humanStdoutSinks,
            _ => [],
        };
        if (sinks.Length == 0)
        {
            return;
        }

        DiagnosticEvent routedEvent = PrepareEvent(diagnosticEvent);
        foreach (IDiagnosticSink sink in sinks)
        {
            sink.Write(routedEvent);
        }
    }

    private static IDiagnosticSink[] CopySinks(
        IEnumerable<IDiagnosticSink> sinks,
        string parameterName
    )
    {
        IDiagnosticSink[] copied = sinks.ToArray();
        if (copied.Any(static sink => sink is null))
        {
            throw new ArgumentException(
                "Diagnostic sinks must not contain null values.",
                parameterName
            );
        }

        return copied;
    }

    private DiagnosticEvent PrepareEvent(DiagnosticEvent diagnosticEvent)
    {
        string message = SafeDiagnosticEnvelopeSanitizer.SanitizeMessage(
            _redactor.Redact(diagnosticEvent.Message)
        );
        IReadOnlyDictionary<string, string?> properties = PrepareProperties(
            diagnosticEvent.Properties
        );

        if (diagnosticEvent.IsSafeDiagnosticEnvelope && string.IsNullOrWhiteSpace(message))
        {
            properties.TryGetValue(
                SafeDiagnosticEnvelopeSanitizer.CodePropertyName,
                out string? safeCode
            );
            message = SafeDiagnosticMessageFallback.Create(
                diagnosticEvent.FallbackScope,
                safeCode,
                safeMessage: null,
                diagnosticEvent.AllowCodeSpecificFallback
            );
        }

        return new DiagnosticEvent(
            diagnosticEvent.Severity,
            diagnosticEvent.Channel,
            message,
            diagnosticEvent.CorrelationId,
            properties,
            diagnosticEvent.Timestamp,
            diagnosticEvent.IsSafeDiagnosticEnvelope
        )
        {
            AllowCodeSpecificFallback = diagnosticEvent.AllowCodeSpecificFallback,
            FallbackScope = diagnosticEvent.FallbackScope,
        };
    }

    private IReadOnlyDictionary<string, string?> PrepareProperties(
        IReadOnlyDictionary<string, string?> properties
    )
    {
        if (properties.Count == 0)
        {
            return properties;
        }

        var prepared = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string?> property in properties.Take(MaxPropertyCount))
        {
            string redactedKey = _redactor.Redact(property.Key) ?? string.Empty;
            string key = SafeDiagnosticEnvelopeSanitizer.IsCanonicalCodePropertyKey(redactedKey)
                ? SafeDiagnosticEnvelopeSanitizer.CodePropertyName
                : SafeDiagnosticEnvelopeSanitizer.SanitizePropertyKey(redactedKey);
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            string? redactedValue = _redactor.Redact(property.Value);
            prepared[key] =
                key == SafeDiagnosticEnvelopeSanitizer.CodePropertyName
                    ? SafeDiagnosticEnvelopeSanitizer.SanitizeCode(redactedValue)
                    : SafeDiagnosticEnvelopeSanitizer.SanitizePropertyValue(redactedValue);
        }

        return prepared;
    }
}
