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

    private sealed class RecordingDiagnosticSink : IDiagnosticSink
    {
        public List<DiagnosticEvent> Events { get; } = [];

        public void Write(DiagnosticEvent diagnosticEvent)
        {
            Events.Add(diagnosticEvent);
        }
    }
}
