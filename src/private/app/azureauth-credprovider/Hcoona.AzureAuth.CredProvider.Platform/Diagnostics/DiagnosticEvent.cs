using System.Collections.ObjectModel;

namespace Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

public sealed class DiagnosticEvent
{
    public DiagnosticEvent(
        DiagnosticSeverity severity,
        DiagnosticChannel channel,
        string message,
        CorrelationId? correlationId = null,
        IReadOnlyDictionary<string, string?>? properties = null,
        DateTimeOffset? timestamp = null,
        bool isSafeDiagnosticEnvelope = false)
    {
        ArgumentNullException.ThrowIfNull(message);

        Severity = severity;
        Channel = channel;
        Message = message;
        CorrelationId = correlationId;
        Timestamp = timestamp ?? DateTimeOffset.UtcNow;
        IsSafeDiagnosticEnvelope = isSafeDiagnosticEnvelope;
        Properties = CopyProperties(properties);
    }

    public DiagnosticSeverity Severity { get; }

    public DiagnosticChannel Channel { get; }

    public string Message { get; }

    public CorrelationId? CorrelationId { get; }

    public IReadOnlyDictionary<string, string?> Properties { get; }

    public DateTimeOffset Timestamp { get; }

    public bool IsSafeDiagnosticEnvelope { get; }

    internal bool AllowCodeSpecificFallback { get; init; }

    internal SafeDiagnosticFallbackScope FallbackScope { get; init; } =
        SafeDiagnosticFallbackScope.AdapterHost;

    private static ReadOnlyDictionary<string, string?> CopyProperties(
        IReadOnlyDictionary<string, string?>? properties)
    {
        if (properties is null || properties.Count == 0)
        {
            return ReadOnlyDictionary<string, string?>.Empty;
        }

        return new ReadOnlyDictionary<string, string?>(new Dictionary<string, string?>(properties));
    }
}

internal enum SafeDiagnosticFallbackScope
{
    AdapterHost,
    CredentialCore,
}
