using System.Reflection;
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
    public void
        RouteSafeEnvelopeRestoresBlankRedactedMessageFromTrustedCredentialCoreUnsupportedFlowCode()
    {
        var diagnosticText = new StringWriter();
        var textSink = new TextWriterDiagnosticSink(diagnosticText);
        var recordingSink = new RecordingDiagnosticSink();
        var router = new DiagnosticRouter([textSink, recordingSink], Redactor("ED"));
        DiagnosticEvent diagnosticEvent = SafeDiagnosticEnvelope(
            "ED",
            new Dictionary<string, string?>
            {
                ["code"] = "UnsupportedFlow",
            },
            allowCodeSpecificFallback: true,
            fallbackScope: SafeDiagnosticFallbackScope.CredentialCore);

        router.Route(diagnosticEvent);

        DiagnosticEvent routedEvent = Assert.Single(recordingSink.Events);
        string diagnosticTextValue = diagnosticText.ToString();
        Assert.Equal(
            "Requested identity flow is not supported by the current MVP policy.",
            routedEvent.Message);
        Assert.Equal("UnsupportedFlow", routedEvent.Properties["code"]);
        Assert.Contains(routedEvent.Message, diagnosticTextValue, StringComparison.Ordinal);
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
    public void RouteNullDiagnosticEventThrowsArgumentNullExceptionBeforeSuppression()
    {
        var sink = new CommitTrackingRecordingDiagnosticSink(returnOutputCommitted: false);
        var router = new DiagnosticRouter([sink], Redactor());

        using (router.BeginUserVisibleCommitTracking(
            suppressDirectCredentialCoreSafeDiagnosticRoutes: true))
        {
            Assert.Throws<ArgumentNullException>(
                "diagnosticEvent",
                () => router.Route(null!));
        }
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

    [Fact]
    public async Task RouteNormalizesClosedFlowedChildScopeToOpenAncestor()
    {
        var sink = new CommitTrackingRecordingDiagnosticSink(returnOutputCommitted: true);
        var router = new DiagnosticRouter([sink], Redactor());
        using var releaseFlowedRoute = new ManualResetEventSlim(false);
        Task? flowedRouteTask = null;
        DiagnosticCommitTrackingScope? parentScope = null;

        using (parentScope = router.BeginUserVisibleCommitTracking())
        {
            using (router.BeginUserVisibleCommitTracking())
            {
                flowedRouteTask = Task.Run(
                    () =>
                    {
                        if (!releaseFlowedRoute.Wait(TimeSpan.FromSeconds(10)))
                        {
                            throw new TimeoutException(
                                "Timed out waiting to release the flowed direct diagnostic route.");
                        }

                        router.Route(new DiagnosticEvent(
                            DiagnosticSeverity.Warning,
                            DiagnosticChannel.Diagnostic,
                            "normalized direct diagnostic"));
                    },
                    TestContext.Current.CancellationToken);
            }

            releaseFlowedRoute.Set();

            await Assert
                .IsType<Task>(flowedRouteTask)
                .WaitAsync(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken);
            Assert.True(parentScope.OutputCommitted);
        }

        DiagnosticEvent diagnosticEvent = Assert.Single(sink.Events);
        Assert.Equal("normalized direct diagnostic", diagnosticEvent.Message);
    }

    [Fact]
    public async Task DisposeSuppressesNewOutputAcrossClosedIntermediateScope()
    {
        var sink = new CommitTrackingRecordingDiagnosticSink(returnOutputCommitted: true);
        var router = new DiagnosticRouter([sink], Redactor());
        using var descendantScopeOpened = new ManualResetEventSlim(false);
        using var releaseDescendantRoute = new ManualResetEventSlim(false);
        Task? flowedTask = null;
        DiagnosticCommitTrackingScope? parentScope = null;

        using (parentScope = router.BeginUserVisibleCommitTracking())
        {
            using (router.BeginUserVisibleCommitTracking())
            {
                flowedTask = Task.Run(
                    () =>
                    {
                        using (router.BeginUserVisibleCommitTracking())
                        {
                            descendantScopeOpened.Set();
                            if (!releaseDescendantRoute.Wait(TimeSpan.FromSeconds(10)))
                            {
                                throw new TimeoutException(
                                    "Timed out waiting to release the descendant " +
                                    "diagnostic route.");
                            }

                            Assert.False(router.RouteWithCommitTracking(new DiagnosticEvent(
                                DiagnosticSeverity.Warning,
                                DiagnosticChannel.Diagnostic,
                                "descendant diagnostic")));
                        }
                    },
                    TestContext.Current.CancellationToken);

                if (!descendantScopeOpened.Wait(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken))
                {
                    throw new TimeoutException(
                        "Timed out waiting for the descendant commit tracking scope to open.");
                }
            }

            releaseDescendantRoute.Set();

            await Assert
                .IsType<Task>(flowedTask)
                .WaitAsync(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken);
            Assert.False(parentScope.OutputCommitted);
        }

        Assert.Empty(sink.Events);
    }

    [Fact]
    public void RouteWithCommitTrackingMarksOpenAncestorCommittedBeforeDescendantScopeDisposes()
    {
        var sink = new CommitTrackingRecordingDiagnosticSink(returnOutputCommitted: true);
        var router = new DiagnosticRouter([sink], Redactor());
        DiagnosticCommitTrackingScope? parentScope = null;

        using (parentScope = router.BeginUserVisibleCommitTracking())
        {
            using (router.BeginUserVisibleCommitTracking())
            {
                Assert.True(router.RouteWithCommitTracking(new DiagnosticEvent(
                    DiagnosticSeverity.Warning,
                    DiagnosticChannel.Diagnostic,
                    "descendant committed diagnostic")));
                Assert.True(parentScope.OutputCommitted);
            }
        }

        DiagnosticEvent diagnosticEvent = Assert.Single(sink.Events);
        Assert.Equal("descendant committed diagnostic", diagnosticEvent.Message);
    }

    [Fact]
    public async Task
        RouteWithCommitTrackingMarksOpenAncestorCommittedWhileDescendantRouteIsInFlight()
    {
        using var sink = new BlockingCommitTrackingDiagnosticSink(
            "in-flight descendant diagnostic");
        var router = new DiagnosticRouter([sink], Redactor());
        Task<bool>? descendantRouteTask = null;
        DiagnosticCommitTrackingScope? parentScope = null;

        try
        {
            using (parentScope = router.BeginUserVisibleCommitTracking())
            {
                using (router.BeginUserVisibleCommitTracking())
                {
                    descendantRouteTask = Task.Run(
                        () => router.RouteWithCommitTracking(new DiagnosticEvent(
                            DiagnosticSeverity.Warning,
                            DiagnosticChannel.Diagnostic,
                            "in-flight descendant diagnostic")),
                        TestContext.Current.CancellationToken);
                    sink.WaitForBlockedWriteEntered();
                    Assert.True(parentScope.OutputCommitted);
                }
            }
        }
        finally
        {
            sink.ReleaseBlockedWrite();
        }

        bool outputCommitted = await Assert
            .IsType<Task<bool>>(descendantRouteTask)
            .WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);
        Assert.False(outputCommitted);
        DiagnosticEvent diagnosticEvent = Assert.Single(sink.AttemptedEvents);
        Assert.Equal("in-flight descendant diagnostic", diagnosticEvent.Message);
    }

    [Fact]
    public async Task
        RouteWithCommitTrackingDoesNotPartiallyPublishDescendantRoutesBeforeAncestorVisibility()
    {
        FieldInfo? stateGateField = typeof(DiagnosticCommitTrackingScope).GetField(
            "_stateGate",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(stateGateField);

        var sink = new CommitTrackingRecordingDiagnosticSink(returnOutputCommitted: false);
        var router = new DiagnosticRouter([sink], Redactor());
        DiagnosticCommitTrackingScope? parentScope = null;
        DiagnosticCommitTrackingScope? childScope = null;
        bool childPublishedBeforeRelease;
        bool parentPublishedBeforeRelease;
        Task<bool>? descendantRouteTask = null;

        using (parentScope = router.BeginUserVisibleCommitTracking())
        {
            using (childScope = router.BeginUserVisibleCommitTracking())
            {
                object stateGate = Assert.IsType<object>(stateGateField.GetValue(parentScope));
                Monitor.Enter(stateGate);
                try
                {
                    descendantRouteTask = Task.Run(
                        () => router.RouteWithCommitTracking(new DiagnosticEvent(
                            DiagnosticSeverity.Warning,
                            DiagnosticChannel.Diagnostic,
                            "partially published descendant diagnostic")),
                        TestContext.Current.CancellationToken);
                    Task<bool> routeTask = Assert.IsType<Task<bool>>(descendantRouteTask);
                    _ = SpinWait.SpinUntil(
                        () => childScope.OutputCommitted || routeTask.IsCompleted,
                        TimeSpan.FromSeconds(1));
                    childPublishedBeforeRelease = childScope.OutputCommitted;
                    parentPublishedBeforeRelease = parentScope.OutputCommitted;
                }
                finally
                {
                    Monitor.Exit(stateGate);
                }

                bool outputCommitted = await Assert
                    .IsType<Task<bool>>(descendantRouteTask)
                    .WaitAsync(
                        TimeSpan.FromSeconds(10),
                        TestContext.Current.CancellationToken);
                Assert.False(outputCommitted);
            }
        }

        DiagnosticEvent diagnosticEvent = Assert.Single(sink.Events);
        Assert.Equal("partially published descendant diagnostic", diagnosticEvent.Message);
        Assert.False(childPublishedBeforeRelease && !parentPublishedBeforeRelease);
    }

    [Fact]
    public async Task RouteSuppressesLateDirectFlowedWritesFromDisposedScopeWithoutOpenAncestor()
    {
        var sink = new CommitTrackingRecordingDiagnosticSink(returnOutputCommitted: true);
        var router = new DiagnosticRouter([sink], Redactor());
        using var releaseLateRoute = new ManualResetEventSlim(false);
        Task? lateRouteTask = null;

        using (router.BeginUserVisibleCommitTracking())
        {
            lateRouteTask = Task.Run(
                () =>
                {
                    if (!releaseLateRoute.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException(
                            "Timed out waiting to release the late direct diagnostic route.");
                    }

                    router.Route(new DiagnosticEvent(
                        DiagnosticSeverity.Warning,
                        DiagnosticChannel.Diagnostic,
                        "late direct diagnostic"));
                },
                TestContext.Current.CancellationToken);
        }

        releaseLateRoute.Set();

        await Assert
            .IsType<Task>(lateRouteTask)
            .WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);
        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task RouteSuppressesNewDescendantRouteWhenAncestorDisposedBeforeEntry()
    {
        var sink = new CommitTrackingRecordingDiagnosticSink(returnOutputCommitted: true);
        var router = new DiagnosticRouter([sink], Redactor());
        using var descendantScopeOpened = new ManualResetEventSlim(false);
        using var releaseDescendantRoute = new ManualResetEventSlim(false);
        Task? descendantRouteTask = null;

        using (router.BeginUserVisibleCommitTracking())
        {
            descendantRouteTask = Task.Run(
                () =>
                {
                    using (router.BeginUserVisibleCommitTracking())
                    {
                        descendantScopeOpened.Set();
                        if (!releaseDescendantRoute.Wait(TimeSpan.FromSeconds(10)))
                        {
                            throw new TimeoutException(
                                "Timed out waiting to release the descendant " +
                                "diagnostic route.");
                        }

                        router.Route(new DiagnosticEvent(
                            DiagnosticSeverity.Warning,
                            DiagnosticChannel.Diagnostic,
                            "late descendant diagnostic"));
                    }
                },
                TestContext.Current.CancellationToken);

            if (!descendantScopeOpened.Wait(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken))
            {
                throw new TimeoutException(
                    "Timed out waiting for the descendant commit tracking scope to open.");
            }
        }

        releaseDescendantRoute.Set();

        await Assert
            .IsType<Task>(descendantRouteTask)
            .WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);
        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task RouteWithCommitTrackingSuppressesLateFlowedWritesFromDisposedScope()
    {
        var sink = new CommitTrackingRecordingDiagnosticSink(returnOutputCommitted: true);
        var router = new DiagnosticRouter([sink], Redactor());
        using var releaseLateRoute = new ManualResetEventSlim(false);
        Task<bool>? lateRouteTask = null;

        using (router.BeginUserVisibleCommitTracking())
        {
            lateRouteTask = Task.Run(() =>
            {
                if (!releaseLateRoute.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException(
                        "Timed out waiting to release a disposed-scope diagnostic route.");
                }

                return router.RouteWithCommitTracking(new DiagnosticEvent(
                    DiagnosticSeverity.Warning,
                    DiagnosticChannel.Diagnostic,
                    "late diagnostic"));
            });
        }

        releaseLateRoute.Set();

        bool outputCommitted = await Assert
            .IsType<Task<bool>>(lateRouteTask)
            .WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);
        Assert.False(outputCommitted);
        Assert.Empty(sink.Events);
    }

    [Fact]
    public async Task BeginUserVisibleCommitTrackingIgnoresInheritedDisposedParentScope()
    {
        var sink = new CommitTrackingRecordingDiagnosticSink(returnOutputCommitted: false);
        var router = new DiagnosticRouter([sink], Redactor());
        using var releaseFlowedTask = new ManualResetEventSlim(false);
        Task? flowedTask = null;

        using (router.BeginUserVisibleCommitTracking())
        {
            flowedTask = Task.Run(
                () =>
                {
                    if (!releaseFlowedTask.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException(
                            "Timed out waiting to release the flowed commit tracking scope.");
                    }

                    using (router.BeginUserVisibleCommitTracking())
                    {
                    }

                    router.Route(new DiagnosticEvent(
                        DiagnosticSeverity.Warning,
                        DiagnosticChannel.Diagnostic,
                        "plain diagnostic after reopened tracking"));
                },
                TestContext.Current.CancellationToken);
        }

        releaseFlowedTask.Set();

        await Assert
            .IsType<Task>(flowedTask)
            .WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);
        DiagnosticEvent diagnosticEvent = Assert.Single(sink.Events);
        Assert.Equal("plain diagnostic after reopened tracking", diagnosticEvent.Message);
    }

    [Fact]
    public async Task BeginUserVisibleCommitTrackingPreservesOpenAncestorWhenPruningClosedScope()
    {
        var sink = new CommitTrackingRecordingDiagnosticSink(returnOutputCommitted: true);
        var router = new DiagnosticRouter([sink], Redactor());
        using var releaseFlowedTask = new ManualResetEventSlim(false);
        Task? flowedTask = null;
        DiagnosticCommitTrackingScope? parentScope = null;

        using (parentScope = router.BeginUserVisibleCommitTracking())
        {
            using (router.BeginUserVisibleCommitTracking())
            {
                flowedTask = Task.Run(
                    () =>
                    {
                        if (!releaseFlowedTask.Wait(TimeSpan.FromSeconds(10)))
                        {
                            throw new TimeoutException(
                                "Timed out waiting to release the flowed commit tracking scope.");
                        }

                        using (router.BeginUserVisibleCommitTracking())
                        {
                            Assert.True(router.RouteWithCommitTracking(new DiagnosticEvent(
                                DiagnosticSeverity.Warning,
                                DiagnosticChannel.Diagnostic,
                                "reopened diagnostic")));
                        }
                    },
                    TestContext.Current.CancellationToken);
            }

            releaseFlowedTask.Set();

            await Assert
                .IsType<Task>(flowedTask)
                .WaitAsync(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken);
            Assert.True(parentScope.OutputCommitted);
        }

        DiagnosticEvent diagnosticEvent = Assert.Single(sink.Events);
        Assert.Equal("reopened diagnostic", diagnosticEvent.Message);
    }

    [Fact]
    public void
        BeginUserVisibleCommitTrackingInheritsLateCoreRecoverySuppressionFromOpenAncestor()
    {
        var router = new DiagnosticRouter([], Redactor());

        using var outerScope = router.BeginUserVisibleCommitTracking();
        using (var firstChildScope = router.BeginUserVisibleCommitTracking())
        {
            firstChildScope.SuppressLateCredentialCoreRecovery();
        }

        Assert.True(outerScope.SuppressesLateCredentialCoreRecovery);

        using var secondChildScope = router.BeginUserVisibleCommitTracking();
        Assert.True(secondChildScope.SuppressesLateCredentialCoreRecovery);
    }

    [Fact]
    public void
        RouteSuppressesDirectCredentialCoreSafeEnvelopesWhileProtocolSuppressionScopeIsActive()
    {
        var sink = new CommitTrackingRecordingDiagnosticSink(returnOutputCommitted: false);
        var router = new DiagnosticRouter([sink], Redactor());

        using (router.BeginUserVisibleCommitTracking(
            suppressDirectCredentialCoreSafeDiagnosticRoutes: true))
        {
            router.Route(new DiagnosticEvent(
                DiagnosticSeverity.Warning,
                DiagnosticChannel.Diagnostic,
                "credential core diagnostic",
                properties: new Dictionary<string, string?>
                {
                    ["code"] = "FlowDisabled",
                },
                isSafeDiagnosticEnvelope: true)
            {
                AllowCodeSpecificFallback = true,
                FallbackScope = SafeDiagnosticFallbackScope.CredentialCore,
            });

            _ = router.RouteWithCommitTracking(new DiagnosticEvent(
                DiagnosticSeverity.Error,
                DiagnosticChannel.Diagnostic,
                "host-owned diagnostic",
                properties: new Dictionary<string, string?>
                {
                    ["code"] = "FlowDisabled",
                },
                isSafeDiagnosticEnvelope: true)
            {
                AllowCodeSpecificFallback = true,
                FallbackScope = SafeDiagnosticFallbackScope.CredentialCore,
            });
        }

        DiagnosticEvent routedEvent = Assert.Single(sink.Events);
        Assert.Equal("host-owned diagnostic", routedEvent.Message);
    }

    [Fact]
    public async Task DisposeMarksInFlightRouteAsCommittedAndSuppressesLateFlowedRoutes()
    {
        using var sink = new BlockingCommitTrackingDiagnosticSink(
            blockedMessage: "in-flight diagnostic");
        var router = new DiagnosticRouter([sink], Redactor());
        using var releaseLateRoute = new ManualResetEventSlim(false);
        Task<bool>? inFlightRouteTask = null;
        Task<bool>? lateRouteTask = null;
        DiagnosticCommitTrackingScope? scope = null;

        using (scope = router.BeginUserVisibleCommitTracking())
        {
            inFlightRouteTask = Task.Run(
                () => router.RouteWithCommitTracking(new DiagnosticEvent(
                    DiagnosticSeverity.Warning,
                    DiagnosticChannel.Diagnostic,
                    "in-flight diagnostic")),
                TestContext.Current.CancellationToken);
            sink.WaitForBlockedWriteEntered();
            lateRouteTask = Task.Run(
                () =>
                {
                    if (!releaseLateRoute.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException(
                            "Timed out waiting to release the late commit-tracked route.");
                    }

                    return router.RouteWithCommitTracking(new DiagnosticEvent(
                        DiagnosticSeverity.Warning,
                        DiagnosticChannel.Diagnostic,
                        "late diagnostic"));
                },
                TestContext.Current.CancellationToken);
        }

        Assert.True(Assert.IsType<DiagnosticCommitTrackingScope>(scope).OutputCommitted);

        releaseLateRoute.Set();
        sink.ReleaseBlockedWrite();

        bool inFlightOutputCommitted = await Assert
            .IsType<Task<bool>>(inFlightRouteTask)
            .WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);
        bool lateOutputCommitted = await Assert
            .IsType<Task<bool>>(lateRouteTask)
            .WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);

        Assert.False(inFlightOutputCommitted);
        Assert.False(lateOutputCommitted);
        DiagnosticEvent diagnosticEvent = Assert.Single(sink.AttemptedEvents);
        Assert.Equal("in-flight diagnostic", diagnosticEvent.Message);
    }

    [Fact]
    public async Task RouteSuppressesLaterSinksWhenAncestorClosesAfterDescendantRouteEntry()
    {
        using var firstSink = new BlockingCommitTrackingDiagnosticSink(
            blockedMessage: "in-flight descendant diagnostic");
        var secondSink = new CommitTrackingRecordingDiagnosticSink(returnOutputCommitted: true);
        var router = new DiagnosticRouter([firstSink, secondSink], Redactor());
        using var descendantScopeOpened = new ManualResetEventSlim(false);
        Task? descendantRouteTask = null;
        DiagnosticCommitTrackingScope? parentScope = null;

        try
        {
            using (parentScope = router.BeginUserVisibleCommitTracking())
            {
                descendantRouteTask = Task.Run(
                    () =>
                    {
                        using (router.BeginUserVisibleCommitTracking())
                        {
                            descendantScopeOpened.Set();
                            router.Route(new DiagnosticEvent(
                                DiagnosticSeverity.Warning,
                                DiagnosticChannel.Diagnostic,
                                "in-flight descendant diagnostic"));
                        }
                    },
                    TestContext.Current.CancellationToken);

                if (!descendantScopeOpened.Wait(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken))
                {
                    throw new TimeoutException(
                        "Timed out waiting for the descendant commit tracking scope to open.");
                }

                firstSink.WaitForBlockedWriteEntered();
            }

            Assert.True(Assert.IsType<DiagnosticCommitTrackingScope>(parentScope).OutputCommitted);
        }
        finally
        {
            firstSink.ReleaseBlockedWrite();
        }

        await Assert
            .IsType<Task>(descendantRouteTask)
            .WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);
        DiagnosticEvent diagnosticEvent = Assert.Single(firstSink.AttemptedEvents);
        Assert.Equal("in-flight descendant diagnostic", diagnosticEvent.Message);
        Assert.Empty(secondSink.Events);
    }

    [Fact]
    public async Task RouteSuppressesLaterSinkWhenAncestorClosesBeforeLaterSinkAdmission()
    {
        FieldInfo? stateGateField = typeof(DiagnosticCommitTrackingScope).GetField(
            "_stateGate",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(stateGateField);

        using var firstSink = new BlockingCommitTrackingDiagnosticSink(
            blockedMessage: "in-flight descendant diagnostic");
        var secondSink = new CommitTrackingRecordingDiagnosticSink(returnOutputCommitted: true);
        var router = new DiagnosticRouter([firstSink, secondSink], Redactor());
        Task? descendantRouteTask = null;
        DiagnosticCommitTrackingScope? parentScope = null;

        try
        {
            using (parentScope = router.BeginUserVisibleCommitTracking())
            {
                descendantRouteTask = Task.Run(
                    () =>
                    {
                        using (router.BeginUserVisibleCommitTracking())
                        {
                            router.Route(new DiagnosticEvent(
                                DiagnosticSeverity.Warning,
                                DiagnosticChannel.Diagnostic,
                                "in-flight descendant diagnostic"));
                        }
                    },
                    TestContext.Current.CancellationToken);

                firstSink.WaitForBlockedWriteEntered();

                object stateGate = Assert.IsType<object>(stateGateField.GetValue(parentScope));
                Monitor.Enter(stateGate);
                try
                {
                    firstSink.ReleaseBlockedWrite();
                    parentScope.Dispose();
                }
                finally
                {
                    Monitor.Exit(stateGate);
                }
            }

            Assert.True(Assert.IsType<DiagnosticCommitTrackingScope>(parentScope).OutputCommitted);
        }
        finally
        {
            firstSink.ReleaseBlockedWrite();
        }

        await Assert
            .IsType<Task>(descendantRouteTask)
            .WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);
        DiagnosticEvent diagnosticEvent = Assert.Single(firstSink.AttemptedEvents);
        Assert.Equal("in-flight descendant diagnostic", diagnosticEvent.Message);
        Assert.Empty(secondSink.Events);
    }

    [Fact]
    public void OutputCommittedIncludesCommittedAncestorScope()
    {
        var router = new DiagnosticRouter([], Redactor());
        using var parentScope = router.BeginUserVisibleCommitTracking();
        using var childScope = router.BeginUserVisibleCommitTracking();

        Assert.False(childScope.OutputCommitted);
        parentScope.RecordCommit(true);
        Assert.True(childScope.OutputCommitted);
    }

    [Fact]
    public void OutputCommittedIncludesInFlightAncestorScope()
    {
        var router = new DiagnosticRouter([], Redactor());
        using var parentScope = router.BeginUserVisibleCommitTracking();
        Assert.True(parentScope.TryEnterRoute());

        try
        {
            using var childScope = router.BeginUserVisibleCommitTracking();
            Assert.True(childScope.OutputCommitted);
        }
        finally
        {
            parentScope.CompleteRoute(false);
        }
    }

    [Fact]
    public void OutputCommittedObservesConcurrentRecordCommit()
    {
        Assert.True(ObserveOutputCommittedDuringConcurrentCommit(static scope =>
            scope.RecordCommit(true)));
    }

    [Fact]
    public void OutputCommittedObservesConcurrentCompleteRouteCommit()
    {
        Assert.True(ObserveOutputCommittedDuringConcurrentCommit(
            static scope =>
            {
                Assert.True(scope.TryEnterRoute());
                scope.CompleteRoute(true);
            }));
    }

    [Fact]
    public void ConcurrentRecordCommitNeverLosesCommittedOutput()
    {
        const int attemptCount = 64;
        int concurrentFalseCommitCount = Math.Max(4, Environment.ProcessorCount);
        var router = new DiagnosticRouter([], Redactor());

        for (var attempt = 0; attempt < attemptCount; attempt++)
        {
            var scope = new DiagnosticCommitTrackingScope(router, previousScope: null);
            Action[] actions =
            [
                () => scope.RecordCommit(true),
            ];

            Array.Resize(ref actions, concurrentFalseCommitCount + 1);
            for (int index = 1; index < actions.Length; index++)
            {
                actions[index] = () => scope.RecordCommit(false);
            }

            RunSimultaneously(actions);
            Assert.True(scope.OutputCommitted);
        }
    }

    private static SecretRedactor Redactor(params string[] secrets)
    {
        return new SecretRedactor(secrets);
    }

    private static void RunSimultaneously(params Action[] actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        using var ready = new CountdownEvent(actions.Length);
        using var release = new ManualResetEventSlim(false);
        var threads = new Thread[actions.Length];

        for (int index = 0; index < actions.Length; index++)
        {
            Action action = actions[index];
            threads[index] = new Thread(() =>
            {
                ready.Signal();
                release.Wait();
                action();
            });
            threads[index].Start();
        }

        Assert.True(ready.Wait(TimeSpan.FromSeconds(10)));
        release.Set();

        foreach (Thread thread in threads)
        {
            Assert.True(thread.Join(TimeSpan.FromSeconds(10)));
        }
    }

    private static DiagnosticEvent SafeDiagnosticEnvelope(
        string message,
        IReadOnlyDictionary<string, string?> properties,
        bool allowCodeSpecificFallback = false,
        SafeDiagnosticFallbackScope fallbackScope = SafeDiagnosticFallbackScope.AdapterHost)
    {
        return new DiagnosticEvent(
            DiagnosticSeverity.Error,
            DiagnosticChannel.Diagnostic,
            message,
            properties: properties,
            isSafeDiagnosticEnvelope: true)
        {
            AllowCodeSpecificFallback = allowCodeSpecificFallback,
            FallbackScope = fallbackScope,
        };
    }

    private static bool ObserveOutputCommittedDuringConcurrentCommit(
        Action<DiagnosticCommitTrackingScope> commitAction)
    {
        const int attemptCount = 64;
        FieldInfo? stateGateField = typeof(DiagnosticCommitTrackingScope).GetField(
            "_stateGate",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(stateGateField);

        for (var attempt = 0; attempt < attemptCount; attempt++)
        {
            var router = new DiagnosticRouter([], Redactor());
            var scope = new DiagnosticCommitTrackingScope(router, previousScope: null);
            object stateGate = Assert.IsType<object>(stateGateField.GetValue(scope));
            using var readerStarted = new ManualResetEventSlim(false);
            bool? observedOutputCommitted = null;
            Exception? readerException = null;
            var readerThread = new Thread(() =>
            {
                try
                {
                    readerStarted.Set();
                    observedOutputCommitted = scope.OutputCommitted;
                }
                catch (Exception ex)
                {
                    readerException = ex;
                }
            });

            Monitor.Enter(stateGate);
            try
            {
                readerThread.Start();
                Assert.True(readerStarted.Wait(TimeSpan.FromSeconds(10)));
                Thread.Sleep(1);
                commitAction(scope);
            }
            finally
            {
                Monitor.Exit(stateGate);
            }

            Assert.True(readerThread.Join(TimeSpan.FromSeconds(10)));
            Assert.Null(readerException);
            if (scope.OutputCommitted && observedOutputCommitted == false)
            {
                return false;
            }
        }

        return true;
    }

    private sealed class RecordingDiagnosticSink : IDiagnosticSink
    {
        public List<DiagnosticEvent> Events { get; } = [];

        public void Write(DiagnosticEvent diagnosticEvent)
        {
            Events.Add(diagnosticEvent);
        }
    }

    private sealed class CommitTrackingRecordingDiagnosticSink : ICommitTrackingDiagnosticSink
    {
        private readonly bool _returnOutputCommitted;

        public CommitTrackingRecordingDiagnosticSink(bool returnOutputCommitted)
        {
            _returnOutputCommitted = returnOutputCommitted;
        }

        public List<DiagnosticEvent> Events { get; } = [];

        public void Write(DiagnosticEvent diagnosticEvent)
        {
            Events.Add(diagnosticEvent);
        }

        public bool WriteWithCommitTracking(DiagnosticEvent diagnosticEvent)
        {
            Write(diagnosticEvent);
            return _returnOutputCommitted;
        }
    }

    private sealed class BlockingCommitTrackingDiagnosticSink
        : ICommitTrackingDiagnosticSink, IDisposable
    {
        private readonly string _blockedMessage;
        private readonly List<DiagnosticEvent> _attemptedEvents = [];
        private readonly ManualResetEventSlim _blockedWriteEntered = new(false);
        private readonly ManualResetEventSlim _releaseBlockedWrite = new(false);

        public BlockingCommitTrackingDiagnosticSink(string blockedMessage)
        {
            _blockedMessage = blockedMessage;
        }

        public DiagnosticEvent[] AttemptedEvents
        {
            get
            {
                lock (_attemptedEvents)
                {
                    return _attemptedEvents.ToArray();
                }
            }
        }

        public void WaitForBlockedWriteEntered()
        {
            if (!_blockedWriteEntered.Wait(TimeSpan.FromSeconds(10)))
            {
                throw new TimeoutException(
                    "Timed out waiting for the in-flight commit-tracked route.");
            }
        }

        public void ReleaseBlockedWrite()
        {
            _releaseBlockedWrite.Set();
        }

        public void Write(DiagnosticEvent diagnosticEvent)
        {
            throw new NotSupportedException("Commit tracking is required.");
        }

        public bool WriteWithCommitTracking(DiagnosticEvent diagnosticEvent)
        {
            lock (_attemptedEvents)
            {
                _attemptedEvents.Add(diagnosticEvent);
            }

            if (string.Equals(
                diagnosticEvent.Message,
                _blockedMessage,
                StringComparison.Ordinal))
            {
                _blockedWriteEntered.Set();
                if (!_releaseBlockedWrite.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException(
                        "Timed out releasing the in-flight commit-tracked route.");
                }
            }

            return false;
        }

        public void Dispose()
        {
            _blockedWriteEntered.Dispose();
            _releaseBlockedWrite.Dispose();
        }
    }
}
