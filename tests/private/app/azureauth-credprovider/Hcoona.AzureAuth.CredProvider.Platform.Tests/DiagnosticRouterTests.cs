using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;
using Hcoona.AzureAuth.CredProvider.Platform.Redaction;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class DiagnosticRouterTests
{
    [Fact]
    public void RouteSendsDiagnosticEventsToDiagnosticSinks()
    {
        var sink = new CapturingSink();
        var router = new DiagnosticRouter([sink], SecretRedactor.Empty);

        router.Route(
            new DiagnosticEvent(DiagnosticSeverity.Warning, DiagnosticChannel.Diagnostic, "warning")
        );

        DiagnosticEvent routed = Assert.Single(sink.Events);
        Assert.Equal("warning", routed.Message);
    }

    [Fact]
    public void RouteSendsHumanEventsOnlyToHumanSinks()
    {
        var diagnosticSink = new CapturingSink();
        var humanSink = new CapturingSink();
        var router = new DiagnosticRouter([diagnosticSink], SecretRedactor.Empty, [humanSink]);

        router.Route(
            new DiagnosticEvent(
                DiagnosticSeverity.Information,
                DiagnosticChannel.HumanStdout,
                "status"
            )
        );

        Assert.Empty(diagnosticSink.Events);
        Assert.Equal("status", Assert.Single(humanSink.Events).Message);
    }

    [Fact]
    public void RouteNeverRoutesProtocolStdout()
    {
        var diagnosticSink = new CapturingSink();
        var humanSink = new CapturingSink();
        var router = new DiagnosticRouter([diagnosticSink], SecretRedactor.Empty, [humanSink]);

        router.Route(
            new DiagnosticEvent(
                DiagnosticSeverity.Error,
                DiagnosticChannel.ProtocolStdout,
                "protocol"
            )
        );

        Assert.Empty(diagnosticSink.Events);
        Assert.Empty(humanSink.Events);
    }

    [Fact]
    public void RouteRedactsMessagesKeysAndValues()
    {
        var sink = new CapturingSink();
        var router = new DiagnosticRouter([sink], new SecretRedactor(["secret"]));

        router.Route(
            new DiagnosticEvent(
                DiagnosticSeverity.Error,
                DiagnosticChannel.Diagnostic,
                "message secret",
                properties: new Dictionary<string, string?> { ["secret-key"] = "secret-value" }
            )
        );

        DiagnosticEvent routed = Assert.Single(sink.Events);
        Assert.DoesNotContain("secret", routed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(
            routed.Properties,
            property =>
                property.Key.Contains("secret", StringComparison.Ordinal)
                || property.Value?.Contains("secret", StringComparison.Ordinal) == true
        );
    }

    [Fact]
    public void RouteBoundsSafeDiagnosticTextAndProperties()
    {
        var sink = new CapturingSink();
        var router = new DiagnosticRouter([sink], SecretRedactor.Empty);
        var properties = Enumerable
            .Range(0, 30)
            .ToDictionary(
                index => $"property-{index}",
                _ => (string?)new string('v', 500),
                StringComparer.Ordinal
            );

        router.Route(
            new DiagnosticEvent(
                DiagnosticSeverity.Error,
                DiagnosticChannel.Diagnostic,
                new string('m', 500),
                properties: properties,
                isSafeDiagnosticEnvelope: true
            )
        );

        DiagnosticEvent routed = Assert.Single(sink.Events);
        Assert.True(routed.Message.Length <= 256);
        Assert.True(routed.Properties.Count <= 16);
        Assert.All(
            routed.Properties.Values,
            value => Assert.True(value is null || value.Length <= 256)
        );
    }

    [Fact]
    public void RouteUsesGenericFallbackForEmptySafeMessage()
    {
        var sink = new CapturingSink();
        var router = new DiagnosticRouter([sink], SecretRedactor.Empty);

        router.Route(
            new DiagnosticEvent(
                DiagnosticSeverity.Error,
                DiagnosticChannel.Diagnostic,
                string.Empty,
                isSafeDiagnosticEnvelope: true
            )
        );

        Assert.Equal(
            SafeDiagnosticMessageFallback.GenericMessage,
            Assert.Single(sink.Events).Message
        );
    }

    private sealed class CapturingSink : IDiagnosticSink
    {
        public List<DiagnosticEvent> Events { get; } = [];

        public void Write(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }
}
