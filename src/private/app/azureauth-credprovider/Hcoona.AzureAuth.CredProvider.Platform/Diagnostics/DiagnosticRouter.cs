using Hcoona.AzureAuth.CredProvider.Platform.Redaction;

namespace Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

public sealed class DiagnosticRouter
{
    private const string SafeDiagnosticCodePropertyName =
        SafeDiagnosticEnvelopeSanitizer.CodePropertyName;

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

        string? trustedPreRedactionSafeDiagnosticCode =
            TryGetTrustedPreRedactionSafeDiagnosticCode(diagnosticEvent);
        var redactedEvent = Redact(diagnosticEvent);
        var routedEvent = diagnosticEvent.IsSafeDiagnosticEnvelope
            ? SanitizeSafeDiagnosticEnvelope(
                redactedEvent,
                trustedPreRedactionSafeDiagnosticCode)
            : redactedEvent;
        foreach (var sink in sinks)
        {
            sink.Write(routedEvent);
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
        var properties = RedactProperties(
            diagnosticEvent.Properties,
            diagnosticEvent.IsSafeDiagnosticEnvelope);

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
            diagnosticEvent.Timestamp,
            diagnosticEvent.IsSafeDiagnosticEnvelope)
        {
            AllowCodeSpecificFallback = diagnosticEvent.AllowCodeSpecificFallback,
        };
    }

    private IReadOnlyDictionary<string, string?> RedactProperties(
        IReadOnlyDictionary<string, string?> properties,
        bool preserveSafeDiagnosticCodePropertyName)
    {
        if (properties.Count == 0)
        {
            return properties;
        }

        var redacted = new Dictionary<string, string?>(properties.Count);
        var changed = false;
        foreach (var property in properties)
        {
            bool preserveCanonicalSafeDiagnosticCodePropertyName =
                preserveSafeDiagnosticCodePropertyName
                && IsCanonicalSafeDiagnosticCodePropertyKey(property.Key);
            var redactedKey = preserveCanonicalSafeDiagnosticCodePropertyName
                ? SafeDiagnosticCodePropertyName
                : _redactor.Redact(property.Key) ?? string.Empty;

            if (preserveSafeDiagnosticCodePropertyName
                && !preserveCanonicalSafeDiagnosticCodePropertyName
                && IsReservedSafeDiagnosticPropertyKey(redactedKey))
            {
                changed = true;
                continue;
            }

            var redactedValue = _redactor.Redact(property.Value);

            changed |=
                !string.Equals(redactedKey, property.Key, StringComparison.Ordinal) ||
                !string.Equals(redactedValue, property.Value, StringComparison.Ordinal);
            redacted[redactedKey] = redactedValue;
        }

        return changed ? redacted : properties;
    }

    private static bool IsCanonicalSafeDiagnosticCodePropertyKey(string key)
    {
        return SafeDiagnosticEnvelopeSanitizer.IsCanonicalCodePropertyKey(key);
    }

    private static string? TryGetTrustedPreRedactionSafeDiagnosticCode(
        DiagnosticEvent diagnosticEvent)
    {
        return diagnosticEvent.IsSafeDiagnosticEnvelope
               && diagnosticEvent.AllowCodeSpecificFallback
            ? TryGetCanonicalSafeDiagnosticCode(diagnosticEvent.Properties)
            : null;
    }

    private static DiagnosticEvent SanitizeSafeDiagnosticEnvelope(
        DiagnosticEvent diagnosticEvent,
        string? trustedPreRedactionSafeDiagnosticCode)
    {
        IReadOnlyDictionary<string, string?> properties =
            SanitizeSafeDiagnosticEnvelopeProperties(diagnosticEvent.Properties);
        string message = RestoreSafeDiagnosticEnvelopeMessage(
            SafeDiagnosticEnvelopeSanitizer.SanitizeMessage(diagnosticEvent.Message),
            properties,
            trustedPreRedactionSafeDiagnosticCode,
            diagnosticEvent.AllowCodeSpecificFallback);

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
            diagnosticEvent.Timestamp,
            isSafeDiagnosticEnvelope: true)
        {
            AllowCodeSpecificFallback = diagnosticEvent.AllowCodeSpecificFallback,
        };
    }

    private static string RestoreSafeDiagnosticEnvelopeMessage(
        string message,
        IReadOnlyDictionary<string, string?> properties,
        string? trustedPreRedactionSafeDiagnosticCode,
        bool allowCodeSpecificFallback)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            return message;
        }

        return SafeDiagnosticMessageFallback.Create(
            ResolveSafeDiagnosticFallbackCode(
                properties,
                trustedPreRedactionSafeDiagnosticCode,
                allowCodeSpecificFallback),
            safeMessage: null,
            allowCodeSpecificFallback);
    }

    private static string? ResolveSafeDiagnosticFallbackCode(
        IReadOnlyDictionary<string, string?> properties,
        string? trustedPreRedactionSafeDiagnosticCode,
        bool allowCodeSpecificFallback)
    {
        return allowCodeSpecificFallback
               && !string.IsNullOrWhiteSpace(trustedPreRedactionSafeDiagnosticCode)
            ? trustedPreRedactionSafeDiagnosticCode
            : TryGetCanonicalSafeDiagnosticCode(properties);
    }

    private static string? TryGetCanonicalSafeDiagnosticCode(
        IReadOnlyDictionary<string, string?> properties)
    {
        return properties.TryGetValue(SafeDiagnosticCodePropertyName, out string? safeCode)
            ? safeCode
            : null;
    }

    private static IReadOnlyDictionary<string, string?> SanitizeSafeDiagnosticEnvelopeProperties(
        IReadOnlyDictionary<string, string?> redactedProperties)
    {
        if (redactedProperties.Count == 0)
        {
            return redactedProperties;
        }

        var sanitized = new Dictionary<string, string?>(
            redactedProperties.Count,
            StringComparer.Ordinal);
        foreach (KeyValuePair<string, string?> property in redactedProperties)
        {
            if (IsCanonicalSafeDiagnosticCodePropertyKey(property.Key))
            {
                string? sanitizedCode = SafeDiagnosticEnvelopeSanitizer.SanitizeCode(
                    property.Value);
                if (!string.IsNullOrEmpty(sanitizedCode))
                {
                    sanitized[SafeDiagnosticCodePropertyName] = sanitizedCode;
                }

                continue;
            }

            string sanitizedKey = SafeDiagnosticEnvelopeSanitizer.SanitizePropertyKey(
                property.Key);
            if (string.IsNullOrEmpty(sanitizedKey)
                || IsReservedSafeDiagnosticPropertyKey(sanitizedKey))
            {
                continue;
            }

            sanitized[sanitizedKey] = SafeDiagnosticEnvelopeSanitizer.SanitizePropertyValue(
                property.Value);
        }

        return DictionaryContentsEqual(redactedProperties, sanitized)
            ? redactedProperties
            : sanitized;
    }

    private static bool DictionaryContentsEqual(
        IReadOnlyDictionary<string, string?> first,
        IReadOnlyDictionary<string, string?> second)
    {
        if (ReferenceEquals(first, second))
        {
            return true;
        }

        if (first.Count != second.Count)
        {
            return false;
        }

        using IEnumerator<KeyValuePair<string, string?>> firstEnumerator = first.GetEnumerator();
        using IEnumerator<KeyValuePair<string, string?>> secondEnumerator = second.GetEnumerator();
        while (firstEnumerator.MoveNext())
        {
            if (!secondEnumerator.MoveNext())
            {
                return false;
            }

            KeyValuePair<string, string?> firstPair = firstEnumerator.Current;
            KeyValuePair<string, string?> secondPair = secondEnumerator.Current;
            if (!string.Equals(firstPair.Key, secondPair.Key, StringComparison.Ordinal) ||
                !string.Equals(firstPair.Value, secondPair.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return !secondEnumerator.MoveNext();
    }

    private static bool IsReservedSafeDiagnosticPropertyKey(string key)
    {
        return SafeDiagnosticEnvelopeSanitizer.IsReservedPropertyKey(key);
    }
}
