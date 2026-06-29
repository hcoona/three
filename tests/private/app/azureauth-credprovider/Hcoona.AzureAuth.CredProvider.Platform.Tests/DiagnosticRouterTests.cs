using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;
using Hcoona.AzureAuth.CredProvider.Platform.Redaction;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class DiagnosticRouterTests
{
    [Fact]
    public void RouteRedactsMessagesAndProperties()
    {
        var sink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter([sink], Redactor("secret"));
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "token=secret",
            properties: new Dictionary<string, string?>
            {
                ["authorization"] = "Bearer secret",
                ["secret-key"] = "safe",
            });

        router.Route(diagnosticEvent);

        var routedEvent = Assert.Single(sink.Events);
        Assert.Equal($"token={SecretRedactor.DefaultMask}", routedEvent.Message);
        Assert.Equal(
            $"Bearer {SecretRedactor.DefaultMask}",
            routedEvent.Properties["authorization"]);
        Assert.Equal("safe", routedEvent.Properties[$"{SecretRedactor.DefaultMask}-key"]);
    }

    [Fact]
    public void RoutePostSanitizesRedactionSynthesizedSafeDiagnosticCodeTokens()
    {
        var sink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter([sink], Redactor("ED"));
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Error,
            DiagnosticChannel.Diagnostic,
            "coEDde=Spoofed",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "coEDde=Spoofed",
                ["detail-coEDde"] = "dropped",
                ["code"] = "Fatal",
            },
            isSafeDiagnosticEnvelope: true);

        router.Route(diagnosticEvent);

        var routedEvent = Assert.Single(sink.Events);
        Assert.Equal("code:Spoofed", routedEvent.Message);
        Assert.Equal("code:Spoofed", routedEvent.Properties["detail"]);
        Assert.Equal("Fatal", routedEvent.Properties["code"]);
        Assert.False(routedEvent.Properties.ContainsKey("detail-code"));
        Assert.DoesNotContain("code=Spoofed", routedEvent.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "code=Spoofed",
            routedEvent.Properties["detail"],
            StringComparison.Ordinal);
    }

    [Fact]
    public void RoutePreservesCanonicalSafeCodePropertyWithRedactedSanitizedValue()
    {
        var sink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter([sink], Redactor("ED"));
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Error,
            DiagnosticChannel.Diagnostic,
            "safe diagnostic",
            properties: new Dictionary<string, string?>
            {
                ["code"] = "coEDde=Spoofed",
            },
            isSafeDiagnosticEnvelope: true);

        router.Route(diagnosticEvent);

        var routedEvent = Assert.Single(sink.Events);
        Assert.True(routedEvent.Properties.ContainsKey("code"));
        Assert.Equal("code_Spoofed", routedEvent.Properties["code"]);
    }

    [Fact]
    public void RouteSafeEnvelopeKeepsCanonicalCodeWhenRedactionSynthesizesSpoofedCodeKey()
    {
        var sink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter([sink], Redactor("ED"));
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Error,
            DiagnosticChannel.Diagnostic,
            "safe diagnostic",
            properties: new Dictionary<string, string?>
            {
                ["code"] = "Fatal",
                ["coEDde"] = "Spoofed",
            },
            isSafeDiagnosticEnvelope: true);

        router.Route(diagnosticEvent);

        var routedEvent = Assert.Single(sink.Events);
        Assert.Single(routedEvent.Properties);
        Assert.Equal("Fatal", routedEvent.Properties["code"]);
    }

    [Fact]
    public void RouteSafeEnvelopeDropsRedactionSynthesizedSpoofedCodeKeyWithoutCanonicalCode()
    {
        var sink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter([sink], Redactor("ED"));
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Error,
            DiagnosticChannel.Diagnostic,
            "safe diagnostic",
            properties: new Dictionary<string, string?>
            {
                ["coEDde"] = "Spoofed",
            },
            isSafeDiagnosticEnvelope: true);

        router.Route(diagnosticEvent);

        DiagnosticEvent routedEvent = Assert.Single(sink.Events);
        Assert.Empty(routedEvent.Properties);
    }

    [Fact]
    public void RouteSafeEnvelopeDropsRedactionEmptiedKeyBeforeTextWriterEmission()
    {
        var diagnosticText = new StringWriter();
        var textSink = new TextWriterDiagnosticSink(diagnosticText);
        var recordingSink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter([textSink, recordingSink], Redactor("ED"));
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Error,
            DiagnosticChannel.Diagnostic,
            "safe diagnostic",
            properties: new Dictionary<string, string?>
            {
                ["ED"] = "should-not-emit",
                ["code"] = "Fatal",
            },
            isSafeDiagnosticEnvelope: true);

        router.Route(diagnosticEvent);

        DiagnosticEvent routedEvent = Assert.Single(recordingSink.Events);
        string diagnosticTextValue = diagnosticText.ToString();
        Assert.False(routedEvent.Properties.ContainsKey(string.Empty));
        Assert.Equal("Fatal", routedEvent.Properties["code"]);
        Assert.DoesNotContain("should-not-emit", diagnosticTextValue, StringComparison.Ordinal);
        Assert.DoesNotContain(" =", diagnosticTextValue, StringComparison.Ordinal);
    }

    [Fact]
    public void RouteSafeEnvelopeReboundsRedactionExpandedMessageCodeAndValue()
    {
        var sink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter([sink], Redactor("x"));
        string safeMessage = new string('m', 250) + "x";
        string safeCode = new string('c', 60) + "x";
        string safeDetail = new string('d', 250) + "x";
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Error,
            DiagnosticChannel.Diagnostic,
            safeMessage,
            properties: new Dictionary<string, string?>
            {
                ["code"] = safeCode,
                ["detail"] = safeDetail,
            },
            isSafeDiagnosticEnvelope: true);

        router.Route(diagnosticEvent);

        DiagnosticEvent routedEvent = Assert.Single(sink.Events);
        string routedCode = Assert.IsType<string>(routedEvent.Properties["code"]);
        string routedDetail = Assert.IsType<string>(routedEvent.Properties["detail"]);
        Assert.Equal(256, routedEvent.Message.Length);
        Assert.EndsWith("...", routedEvent.Message);
        Assert.Equal(64, routedCode.Length);
        Assert.EndsWith("...", routedCode);
        Assert.Equal(256, routedDetail.Length);
        Assert.EndsWith("...", routedDetail);
    }

    [Fact]
    public void RouteSafeEnvelopeDropsCanonicalCodeWhenRedactionEmptiesValue()
    {
        var diagnosticText = new StringWriter();
        var textSink = new TextWriterDiagnosticSink(diagnosticText);
        var recordingSink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter([textSink, recordingSink], Redactor("ED"));
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Error,
            DiagnosticChannel.Diagnostic,
            "safe diagnostic",
            properties: new Dictionary<string, string?>
            {
                ["code"] = "ED",
                ["detail"] = "available",
            },
            isSafeDiagnosticEnvelope: true);

        router.Route(diagnosticEvent);

        DiagnosticEvent routedEvent = Assert.Single(recordingSink.Events);
        string diagnosticTextValue = diagnosticText.ToString();
        Assert.False(routedEvent.Properties.ContainsKey("code"));
        Assert.Equal("available", routedEvent.Properties["detail"]);
        Assert.DoesNotContain(" code=", diagnosticTextValue, StringComparison.Ordinal);
    }

    [Fact]
    public void RoutePublicSafeEnvelopeDefaultsToGenericFallbackWhenRedactionBlanksMessage()
    {
        var diagnosticText = new StringWriter();
        var textSink = new TextWriterDiagnosticSink(diagnosticText);
        var recordingSink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter([textSink, recordingSink], Redactor("ED"));
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Error,
            DiagnosticChannel.Diagnostic,
            "ED",
            properties: new Dictionary<string, string?>
            {
                ["code"] = "UnsupportedContractMajor",
                ["detail"] = "available",
            },
            isSafeDiagnosticEnvelope: true);
        Assert.False(diagnosticEvent.AllowCodeSpecificFallback);

        router.Route(diagnosticEvent);

        DiagnosticEvent routedEvent = Assert.Single(recordingSink.Events);
        string diagnosticTextValue = diagnosticText.ToString();
        Assert.Equal("Adapter host execution failed.", routedEvent.Message);
        Assert.Equal("UnsupportedContractMajor", routedEvent.Properties["code"]);
        Assert.Equal("available", routedEvent.Properties["detail"]);
        Assert.Contains(
            "Adapter host execution failed.",
            diagnosticTextValue,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Credential result contract major is unsupported.",
            diagnosticTextValue,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RouteSafeEnvelopeRestoresBlankRedactedMessageWithGenericFallbackWhenCodeDrops()
    {
        var diagnosticText = new StringWriter();
        var textSink = new TextWriterDiagnosticSink(diagnosticText);
        var recordingSink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter([textSink, recordingSink], Redactor("ED"));
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Error,
            DiagnosticChannel.Diagnostic,
            "ED",
            properties: new Dictionary<string, string?>
            {
                ["code"] = "ED",
            },
            isSafeDiagnosticEnvelope: true);

        router.Route(diagnosticEvent);

        DiagnosticEvent routedEvent = Assert.Single(recordingSink.Events);
        string diagnosticTextValue = diagnosticText.ToString();
        Assert.Equal("Adapter host execution failed.", routedEvent.Message);
        Assert.Empty(routedEvent.Properties);
        Assert.Contains(
            "Adapter host execution failed.",
            diagnosticTextValue,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        RouteSafeEnvelopeRestoresBlankRedactedMessageWithGenericFallbackForUntrustedCode()
    {
        var diagnosticText = new StringWriter();
        var textSink = new TextWriterDiagnosticSink(diagnosticText);
        var recordingSink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter([textSink, recordingSink], Redactor("ED"));
        DiagnosticEvent diagnosticEvent = SafeDiagnosticEnvelope(
            "ED",
            new Dictionary<string, string?>
            {
                ["code"] = "UnsupportedContractMajor",
                ["detail"] = "available",
            },
            allowCodeSpecificFallback: false);

        router.Route(diagnosticEvent);

        DiagnosticEvent routedEvent = Assert.Single(recordingSink.Events);
        string diagnosticTextValue = diagnosticText.ToString();
        Assert.Equal("Adapter host execution failed.", routedEvent.Message);
        Assert.Equal("UnsupportedContractMajor", routedEvent.Properties["code"]);
        Assert.Equal("available", routedEvent.Properties["detail"]);
        Assert.Contains(
            "Adapter host execution failed.",
            diagnosticTextValue,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Credential result contract major is unsupported.",
            diagnosticTextValue,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RouteSafeEnvelopeKeepsGenericFallbackWhenRedactionCanonicalizesUntrustedCode()
    {
        var diagnosticText = new StringWriter();
        var textSink = new TextWriterDiagnosticSink(diagnosticText);
        var recordingSink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter([textSink, recordingSink], Redactor("ED"));
        DiagnosticEvent diagnosticEvent = SafeDiagnosticEnvelope(
            "ED",
            new Dictionary<string, string?>
            {
                ["code"] = "UnsupportedContractMajorED",
            },
            allowCodeSpecificFallback: false);

        router.Route(diagnosticEvent);

        DiagnosticEvent routedEvent = Assert.Single(recordingSink.Events);
        string diagnosticTextValue = diagnosticText.ToString();
        Assert.Equal("Adapter host execution failed.", routedEvent.Message);
        Assert.Equal("UnsupportedContractMajor", routedEvent.Properties["code"]);
        Assert.Contains(
            "code=UnsupportedContractMajor",
            diagnosticTextValue,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Credential result contract major is unsupported.",
            diagnosticTextValue,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RouteSafeEnvelopeRestoresBlankRedactedMessageFromTrustedValidationCode()
    {
        var diagnosticText = new StringWriter();
        var textSink = new TextWriterDiagnosticSink(diagnosticText);
        var recordingSink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter([textSink, recordingSink], Redactor("ED"));
        DiagnosticEvent diagnosticEvent = SafeDiagnosticEnvelope(
            "ED",
            new Dictionary<string, string?>
            {
                ["code"] = "UnsupportedContractMajor",
                ["detail"] = "available",
            },
            allowCodeSpecificFallback: true);

        router.Route(diagnosticEvent);

        DiagnosticEvent routedEvent = Assert.Single(recordingSink.Events);
        string diagnosticTextValue = diagnosticText.ToString();
        Assert.Equal(
            "Credential result contract major is unsupported.",
            routedEvent.Message);
        Assert.Equal("UnsupportedContractMajor", routedEvent.Properties["code"]);
        Assert.Equal("available", routedEvent.Properties["detail"]);
        Assert.Contains(
            "Credential result contract major is unsupported.",
            diagnosticTextValue,
            StringComparison.Ordinal);
    }

    [Fact]
    public void
        RouteSafeEnvelopeRestoresBlankRedactedMessageFromTrustedValidationCodeWhenCodeDrops()
    {
        var diagnosticText = new StringWriter();
        var textSink = new TextWriterDiagnosticSink(diagnosticText);
        var recordingSink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter(
            [textSink, recordingSink],
            Redactor("ED", "UnsupportedContractMajor"));
        DiagnosticEvent diagnosticEvent = SafeDiagnosticEnvelope(
            "ED",
            new Dictionary<string, string?>
            {
                ["code"] = "UnsupportedContractMajor",
                ["detail"] = "available",
            },
            allowCodeSpecificFallback: true);

        router.Route(diagnosticEvent);

        DiagnosticEvent routedEvent = Assert.Single(recordingSink.Events);
        string diagnosticTextValue = diagnosticText.ToString();
        Assert.Equal(
            "Credential result contract major is unsupported.",
            routedEvent.Message);
        Assert.False(routedEvent.Properties.ContainsKey("code"));
        Assert.Equal("available", routedEvent.Properties["detail"]);
        Assert.Contains(
            "Credential result contract major is unsupported.",
            diagnosticTextValue,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "UnsupportedContractMajor",
            diagnosticTextValue,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RoutePreservesCanonicalSafeCodePropertyName()
    {
        var sink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter([sink], Redactor("od"));
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Error,
            DiagnosticChannel.Diagnostic,
            "safe diagnostic",
            properties: new Dictionary<string, string?>
            {
                ["code"] = "Opaque",
            },
            isSafeDiagnosticEnvelope: true);

        router.Route(diagnosticEvent);

        var routedEvent = Assert.Single(sink.Events);
        Assert.Single(routedEvent.Properties);
        Assert.Equal("Opaque", routedEvent.Properties["code"]);
    }

    [Theory]
    [InlineData(DiagnosticChannel.Diagnostic)]
    [InlineData(DiagnosticChannel.HumanStdout)]
    public void RouteDoesNotPostSanitizeNonSafeEvents(DiagnosticChannel channel)
    {
        var diagnosticSink = new RecordingDiagnosticSink();
        var humanStdoutSink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter(
            [diagnosticSink],
            Redactor("ED"),
            [humanStdoutSink]);
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Error,
            channel,
            "coEDde=Visible",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "coEDde=Visible",
                ["detail-coEDde"] = "kept",
                ["code"] = "coEDde=Visible",
            });

        router.Route(diagnosticEvent);

        RecordingDiagnosticSink sink = channel == DiagnosticChannel.Diagnostic
            ? diagnosticSink
            : humanStdoutSink;
        var routedEvent = Assert.Single(sink.Events);
        Assert.Empty(channel == DiagnosticChannel.Diagnostic
            ? humanStdoutSink.Events
            : diagnosticSink.Events);
        Assert.Equal("code=Visible", routedEvent.Message);
        Assert.Equal("code=Visible", routedEvent.Properties["detail"]);
        Assert.Equal("kept", routedEvent.Properties["detail-code"]);
        Assert.Equal("code=Visible", routedEvent.Properties["code"]);
        Assert.DoesNotContain("code:Visible", routedEvent.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RoutePreservesCorrelationId()
    {
        var sink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter([sink], Redactor("secret"));
        var correlationId = CorrelationId.New();
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "secret",
            correlationId);

        router.Route(diagnosticEvent);

        Assert.Same(correlationId, Assert.Single(sink.Events).CorrelationId);
    }

    [Fact]
    public void ConstructorRequiresExplicitRedactor()
    {
        var sink = new RecordingDiagnosticSink();

        Assert.Throws<ArgumentNullException>(
            "redactor",
            () => new DiagnosticRouter([sink], redactor: null!));
    }

    [Fact]
    public void RouteNeverRoutesProtocolStdoutToDiagnosticOrHumanStdoutSinks()
    {
        var diagnosticSink = new RecordingDiagnosticSink();
        var humanStdoutSink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter(
            [diagnosticSink],
            Redactor("protocol"),
            [humanStdoutSink]);
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.ProtocolStdout,
            "protocol payload with credential");

        router.Route(diagnosticEvent);

        Assert.Empty(diagnosticSink.Events);
        Assert.Empty(humanStdoutSink.Events);
    }

    [Fact]
    public void RouteSendsHumanStdoutOnlyToHumanStdoutSinks()
    {
        var diagnosticSink = new RecordingDiagnosticSink();
        var humanStdoutSink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter(
            [diagnosticSink],
            Redactor("secret"),
            [humanStdoutSink]);
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.HumanStdout,
            "human message secret");

        router.Route(diagnosticEvent);

        Assert.Empty(diagnosticSink.Events);
        Assert.Equal(
            $"human message {SecretRedactor.DefaultMask}",
            Assert.Single(humanStdoutSink.Events).Message);
    }

    [Fact]
    public void RouteDoesNotSendDiagnosticsToHumanStdoutSinks()
    {
        var diagnosticSink = new RecordingDiagnosticSink();
        var humanStdoutSink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter(
            [diagnosticSink],
            Redactor("secret"),
            [humanStdoutSink]);
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "diagnostic message");

        router.Route(diagnosticEvent);

        Assert.Equal("diagnostic message", Assert.Single(diagnosticSink.Events).Message);
        Assert.Empty(humanStdoutSink.Events);
    }

    [Theory]
    [InlineData(DiagnosticSeverity.Warning)]
    [InlineData(DiagnosticSeverity.Error)]
    public void RouteSendsWarningsAndErrorsToDiagnosticSinks(DiagnosticSeverity severity)
    {
        var sink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter([sink], Redactor());
        var diagnosticEvent = new DiagnosticEvent(
            severity,
            DiagnosticChannel.Diagnostic,
            "diagnostic message");

        router.Route(diagnosticEvent);

        Assert.Equal(severity, Assert.Single(sink.Events).Severity);
    }

    [Fact]
    public void RouteSendsIdenticalRedactedEventToMultipleSinks()
    {
        var firstSink = new RecordingDiagnosticSink();
        var secondSink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter(
            [firstSink, secondSink],
            Redactor("secret"));
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Error,
            DiagnosticChannel.Diagnostic,
            "secret",
            properties: new Dictionary<string, string?>
            {
                ["token"] = "secret",
            });

        router.Route(diagnosticEvent);

        var firstEvent = Assert.Single(firstSink.Events);
        var secondEvent = Assert.Single(secondSink.Events);
        Assert.Same(firstEvent, secondEvent);
        Assert.Equal(SecretRedactor.DefaultMask, firstEvent.Message);
        Assert.Equal(SecretRedactor.DefaultMask, firstEvent.Properties["token"]);
    }

    private static SecretRedactor Redactor(params string[] secrets)
    {
        return new SecretRedactor(secrets);
    }

    private static DiagnosticEvent SafeDiagnosticEnvelope(
        string message,
        IReadOnlyDictionary<string, string?> properties,
        bool allowCodeSpecificFallback = false)
    {
        return new DiagnosticEvent(
            DiagnosticSeverity.Error,
            DiagnosticChannel.Diagnostic,
            message,
            properties: properties,
            isSafeDiagnosticEnvelope: true)
        {
            AllowCodeSpecificFallback = allowCodeSpecificFallback,
        };
    }

    private sealed class RecordingDiagnosticSink : IDiagnosticSink
    {
        public List<DiagnosticEvent> Events { get; } = [];

        public void Write(DiagnosticEvent diagnosticEvent)
        {
            Events.Add(diagnosticEvent);
        }
    }
}
