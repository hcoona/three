using System.Runtime.ExceptionServices;
using System.Threading;
using Hcoona.AzureAuth.CredProvider.Platform.Redaction;

namespace Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;

public sealed class DiagnosticRouter
{
    private const string SafeDiagnosticCodePropertyName =
        SafeDiagnosticEnvelopeSanitizer.CodePropertyName;

    private readonly IDiagnosticSink[] _diagnosticSinks;
    private readonly IDiagnosticSink[] _humanStdoutSinks;
    private readonly SecretRedactor _redactor;
    private readonly AsyncLocal<DiagnosticCommitTrackingScope?> _activeCommitTrackingScope = new();

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

        DiagnosticCommitTrackingScope? flowedCommitTrackingScope = ActiveCommitTrackingScope;
        DiagnosticCommitTrackingScope? commitTrackingScope =
            GetOpenCommitTrackingScope(flowedCommitTrackingScope);
        if (ShouldSuppressDirectRoute(diagnosticEvent, commitTrackingScope))
        {
            return;
        }

        if (ShouldSuppressLateFlowedRoute(flowedCommitTrackingScope, commitTrackingScope))
        {
            return;
        }

        try
        {
            _ = RouteCore(
                diagnosticEvent,
                trackUserVisibleCommit: flowedCommitTrackingScope is not null,
                commitTrackingScope);
        }
        catch (DiagnosticWriteException ex)
        {
            ExceptionDispatchInfo.Capture(ex.OriginalException).Throw();
            throw;
        }
    }

    internal DiagnosticCommitTrackingScope BeginUserVisibleCommitTracking(
        bool validateHumanStdoutSinks = true,
        bool suppressDirectCredentialCoreSafeDiagnosticRoutes = false)
    {
        ValidateSinksSupportCommitTracking(_diagnosticSinks, DiagnosticChannel.Diagnostic);
        if (validateHumanStdoutSinks)
        {
            ValidateSinksSupportCommitTracking(_humanStdoutSinks, DiagnosticChannel.HumanStdout);
        }

        DiagnosticCommitTrackingScope? previousScope =
            GetOpenCommitTrackingScope(ActiveCommitTrackingScope);
        DiagnosticCommitTrackingScope scope = new(
            this,
            previousScope,
            suppressDirectCredentialCoreSafeDiagnosticRoutes
                || (previousScope?.SuppressesDirectCredentialCoreSafeDiagnosticRoutes ?? false));
        ActiveCommitTrackingScope = scope;
        return scope;
    }

    internal void PruneClosedActiveCommitTrackingScope()
    {
        ActiveCommitTrackingScope = GetOpenCommitTrackingScope(ActiveCommitTrackingScope);
    }

    internal DiagnosticCommitTrackingScope? CaptureActiveCommitTrackingScope()
    {
        return ActiveCommitTrackingScope;
    }

    internal bool RouteWithCommitTracking(DiagnosticEvent diagnosticEvent)
    {
        DiagnosticCommitTrackingScope? flowedCommitTrackingScope = ActiveCommitTrackingScope;
        DiagnosticCommitTrackingScope? commitTrackingScope =
            GetOpenCommitTrackingScope(flowedCommitTrackingScope);
        if (ShouldSuppressLateFlowedRoute(flowedCommitTrackingScope, commitTrackingScope))
        {
            return false;
        }

        return RouteCore(
            diagnosticEvent,
            trackUserVisibleCommit: true,
            commitTrackingScope);
    }

    private bool RouteCore(
        DiagnosticEvent diagnosticEvent,
        bool trackUserVisibleCommit,
        DiagnosticCommitTrackingScope? commitTrackingScope)
    {
        ArgumentNullException.ThrowIfNull(diagnosticEvent);

        if (trackUserVisibleCommit
            && IsCurrentOrAncestorCommitTrackingScopeClosed(commitTrackingScope))
        {
            return false;
        }

        IDiagnosticSink[] sinks = GetSinks(diagnosticEvent.Channel);
        if (sinks.Length == 0)
        {
            return false;
        }

        DiagnosticEvent routedEvent = CreateRoutedEvent(diagnosticEvent);
        if (!trackUserVisibleCommit)
        {
            foreach (IDiagnosticSink sink in sinks)
            {
                sink.Write(routedEvent);
            }

            return false;
        }

        ValidateSinksSupportCommitTracking(sinks, diagnosticEvent.Channel);

        var userVisibleOutputCommitted = false;
        var routeEntered = false;
        try
        {
            if (commitTrackingScope is not null)
            {
                routeEntered = commitTrackingScope.TryEnterRoute();
                if (!routeEntered)
                {
                    return false;
                }
            }

            foreach (IDiagnosticSink sink in sinks)
            {
                var sinkWriteEntered = false;
                if (commitTrackingScope is not null)
                {
                    sinkWriteEntered = commitTrackingScope.TryEnterSinkWrite();
                    if (!sinkWriteEntered)
                    {
                        return userVisibleOutputCommitted;
                    }
                }

                try
                {
                    try
                    {
                        userVisibleOutputCommitted |=
                            ((ICommitTrackingDiagnosticSink)sink).WriteWithCommitTracking(
                                routedEvent);
                    }
                    finally
                    {
                        if (sinkWriteEntered)
                        {
                            commitTrackingScope!.CompleteSinkWrite();
                        }
                    }
                }
                catch (DiagnosticWriteException ex)
                {
                    userVisibleOutputCommitted |= ex.OutputCommitted;
                    throw new DiagnosticWriteException(
                        userVisibleOutputCommitted,
                        ex.OriginalException);
                }
                catch (Exception ex)
                {
                    throw new DiagnosticWriteException(userVisibleOutputCommitted, ex);
                }
            }

            return userVisibleOutputCommitted;
        }
        finally
        {
            if (routeEntered)
            {
                commitTrackingScope!.CompleteRoute(userVisibleOutputCommitted);
            }
        }
    }

    private IDiagnosticSink[] GetSinks(DiagnosticChannel channel)
    {
        return channel switch
        {
            DiagnosticChannel.Diagnostic => _diagnosticSinks,
            DiagnosticChannel.HumanStdout => _humanStdoutSinks,
            DiagnosticChannel.ProtocolStdout => Array.Empty<IDiagnosticSink>(),
            _ => Array.Empty<IDiagnosticSink>(),
        };
    }

    private static bool ShouldSuppressDirectRoute(
        DiagnosticEvent diagnosticEvent,
        DiagnosticCommitTrackingScope? commitTrackingScope)
    {
        return diagnosticEvent.Channel == DiagnosticChannel.Diagnostic
            && diagnosticEvent.IsSafeDiagnosticEnvelope
            && diagnosticEvent.FallbackScope == SafeDiagnosticFallbackScope.CredentialCore
            && (GetOpenCommitTrackingScope(commitTrackingScope)
                ?.SuppressesDirectCredentialCoreSafeDiagnosticRoutes ?? false);
    }

    private static bool ShouldSuppressLateFlowedRoute(
        DiagnosticCommitTrackingScope? flowedCommitTrackingScope,
        DiagnosticCommitTrackingScope? openCommitTrackingScope)
    {
        return flowedCommitTrackingScope is not null && openCommitTrackingScope is null;
    }

    private DiagnosticEvent CreateRoutedEvent(DiagnosticEvent diagnosticEvent)
    {
        string? trustedPreRedactionSafeDiagnosticCode =
            TryGetTrustedPreRedactionSafeDiagnosticCode(diagnosticEvent);
        DiagnosticEvent redactedEvent = Redact(diagnosticEvent);
        return diagnosticEvent.IsSafeDiagnosticEnvelope
            ? SanitizeSafeDiagnosticEnvelope(
                redactedEvent,
                trustedPreRedactionSafeDiagnosticCode)
            : redactedEvent;
    }

    private static void ValidateSinksSupportCommitTracking(
        IEnumerable<IDiagnosticSink> sinks,
        DiagnosticChannel channel)
    {
        foreach (IDiagnosticSink sink in sinks)
        {
            if (sink is ICommitTrackingDiagnosticSink)
            {
                continue;
            }

            throw new DiagnosticCommitTrackingUnavailableException(channel, sink.GetType());
        }
    }

    private static bool IsClosedCommitTrackingScope(
        DiagnosticCommitTrackingScope? commitTrackingScope)
    {
        return commitTrackingScope is not null && commitTrackingScope.IsClosed;
    }

    private static bool IsCurrentOrAncestorCommitTrackingScopeClosed(
        DiagnosticCommitTrackingScope? commitTrackingScope)
    {
        return commitTrackingScope?.HasClosedScopeInChain() ?? false;
    }

    private static DiagnosticCommitTrackingScope? GetOpenCommitTrackingScope(
        DiagnosticCommitTrackingScope? commitTrackingScope)
    {
        while (IsClosedCommitTrackingScope(commitTrackingScope))
        {
            commitTrackingScope = commitTrackingScope!.PreviousScope;
        }

        return commitTrackingScope;
    }

    internal void RestoreActiveCommitTrackingScope(
        DiagnosticCommitTrackingScope? commitTrackingScope)
    {
        ActiveCommitTrackingScope = GetOpenCommitTrackingScope(commitTrackingScope);
    }

    internal void RestoreCapturedActiveCommitTrackingScope(
        DiagnosticCommitTrackingScope? commitTrackingScope)
    {
        ActiveCommitTrackingScope = commitTrackingScope;
    }

    internal void RecordUserVisibleOutputCommit(bool outputCommitted)
    {
        GetOpenCommitTrackingScope(ActiveCommitTrackingScope)?.RecordCommit(outputCommitted);
    }

    private DiagnosticCommitTrackingScope? ActiveCommitTrackingScope
    {
        get => _activeCommitTrackingScope.Value;
        set => _activeCommitTrackingScope.Value = value;
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
            FallbackScope = diagnosticEvent.FallbackScope,
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
            diagnosticEvent.AllowCodeSpecificFallback,
            diagnosticEvent.FallbackScope);

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
            FallbackScope = diagnosticEvent.FallbackScope,
        };
    }

    private static string RestoreSafeDiagnosticEnvelopeMessage(
        string message,
        IReadOnlyDictionary<string, string?> properties,
        string? trustedPreRedactionSafeDiagnosticCode,
        bool allowCodeSpecificFallback,
        SafeDiagnosticFallbackScope fallbackScope)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            return message;
        }

        return SafeDiagnosticMessageFallback.Create(
            fallbackScope,
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

internal sealed class DiagnosticWriteException : Exception
{
    public DiagnosticWriteException(bool outputCommitted, Exception originalException)
        : base(originalException.Message, originalException)
    {
        ArgumentNullException.ThrowIfNull(originalException);

        OutputCommitted = outputCommitted;
        OriginalException = originalException;
    }

    public bool OutputCommitted { get; }

    public Exception OriginalException { get; }
}

internal sealed class DiagnosticCommitTrackingState
{
    internal bool SuppressesLateCredentialCoreRecovery { get; set; }
}

internal sealed class DiagnosticCommitTrackingScope : IDisposable
{
    private readonly DiagnosticRouter _router;
    private readonly DiagnosticCommitTrackingScope? _previousScope;
    private readonly bool _suppressesDirectCredentialCoreSafeDiagnosticRoutes;
    private readonly object _stateGate;
    private readonly DiagnosticCommitTrackingState _state;
    private int _activeRouteCount;
    private int _activeSinkWriteCount;
    private bool _closed;
    private bool _outputCommitted;

    public DiagnosticCommitTrackingScope(
        DiagnosticRouter router,
        DiagnosticCommitTrackingScope? previousScope,
        bool suppressesDirectCredentialCoreSafeDiagnosticRoutes = false)
    {
        _router = router;
        _previousScope = previousScope;
        _suppressesDirectCredentialCoreSafeDiagnosticRoutes =
            suppressesDirectCredentialCoreSafeDiagnosticRoutes;
        _stateGate = previousScope?._stateGate ?? new object();
        _state = previousScope?._state ?? new DiagnosticCommitTrackingState();
    }

    public bool OutputCommitted
    {
        get
        {
            lock (_stateGate)
            {
                for (DiagnosticCommitTrackingScope? scope = this;
                    scope is not null;
                    scope = scope._previousScope)
                {
                    if (scope._outputCommitted
                        || scope._activeRouteCount != 0
                        || scope._activeSinkWriteCount != 0)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }

    internal DiagnosticCommitTrackingScope? PreviousScope => _previousScope;

    internal bool SuppressesDirectCredentialCoreSafeDiagnosticRoutes =>
        _suppressesDirectCredentialCoreSafeDiagnosticRoutes;

    internal bool SuppressesLateCredentialCoreRecovery
    {
        get
        {
            lock (_stateGate)
            {
                return _state.SuppressesLateCredentialCoreRecovery;
            }
        }
    }

    internal bool IsClosed
    {
        get
        {
            lock (_stateGate)
            {
                return _closed;
            }
        }
    }

    public void Dispose()
    {
        lock (_stateGate)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            if (_outputCommitted || _activeRouteCount != 0 || _activeSinkWriteCount != 0)
            {
                MarkCommittedChain(this);
            }
        }

        _router.RestoreActiveCommitTrackingScope(_previousScope);
    }

    internal void RecordCommit(bool outputCommitted)
    {
        if (!outputCommitted)
        {
            return;
        }

        lock (_stateGate)
        {
            MarkCommittedChain(this);
        }
    }

    internal void SuppressLateCredentialCoreRecovery()
    {
        lock (_stateGate)
        {
            _state.SuppressesLateCredentialCoreRecovery = true;
        }
    }

    internal bool HasClosedScopeInChain()
    {
        lock (_stateGate)
        {
            for (DiagnosticCommitTrackingScope? scope = this;
                scope is not null;
                scope = scope._previousScope)
            {
                if (scope._closed)
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal bool TryEnterSinkWrite()
    {
        lock (_stateGate)
        {
            for (DiagnosticCommitTrackingScope? scope = this;
                scope is not null;
                scope = scope._previousScope)
            {
                if (scope._closed)
                {
                    return false;
                }
            }

            for (DiagnosticCommitTrackingScope? scope = this;
                scope is not null;
                scope = scope._previousScope)
            {
                scope._activeSinkWriteCount++;
            }
        }

        return true;
    }

    internal void CompleteSinkWrite()
    {
        lock (_stateGate)
        {
            for (DiagnosticCommitTrackingScope? scope = this;
                scope is not null;
                scope = scope._previousScope)
            {
                scope._activeSinkWriteCount--;
            }
        }
    }

    internal bool TryEnterRoute()
    {
        lock (_stateGate)
        {
            for (DiagnosticCommitTrackingScope? scope = this;
                scope is not null;
                scope = scope._previousScope)
            {
                if (scope._closed)
                {
                    return false;
                }
            }

            for (DiagnosticCommitTrackingScope? scope = this;
                scope is not null;
                scope = scope._previousScope)
            {
                scope._activeRouteCount++;
            }
        }

        return true;
    }

    internal void CompleteRoute(bool outputCommitted)
    {
        lock (_stateGate)
        {
            for (DiagnosticCommitTrackingScope? scope = this;
                scope is not null;
                scope = scope._previousScope)
            {
                if (outputCommitted)
                {
                    scope._outputCommitted = true;
                }

                scope._activeRouteCount--;
            }
        }
    }

    private static void MarkCommittedChain(DiagnosticCommitTrackingScope? scope)
    {
        while (scope is not null)
        {
            scope._outputCommitted = true;
            scope = scope._previousScope;
        }
    }
}

internal sealed class DiagnosticCommitTrackingUnavailableException : Exception
{
    public DiagnosticCommitTrackingUnavailableException(
        DiagnosticChannel channel,
        Type sinkType)
        : base(
            $"Diagnostic sink '{sinkType.FullName}' does not support commit tracking " +
            $"for '{channel}'.")
    {
        Channel = channel;
        SinkType = sinkType;
    }

    public DiagnosticChannel Channel { get; }

    public Type SinkType { get; }
}
