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
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(message);

        Severity = severity;
        Channel = channel;
        Message = message;
        CorrelationId = correlationId;
        Timestamp = timestamp ?? DateTimeOffset.UtcNow;
        Properties = CopyProperties(properties);
    }

    public DiagnosticSeverity Severity { get; }

    public DiagnosticChannel Channel { get; }

    public string Message { get; }

    public CorrelationId? CorrelationId { get; }

    public IReadOnlyDictionary<string, string?> Properties { get; }

    public DateTimeOffset Timestamp { get; }

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
