using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class TextWriterDiagnosticSinkTests
{
    [Fact]
    public void WriteFormatsHumanReadableDiagnosticLine()
    {
        var writer = new StringWriter();
        var sink = new TextWriterDiagnosticSink(writer, DiagnosticSeverity.Warning);
        var correlationId = CorrelationId.FromGuid(
            Guid.Parse("9f2ea1a1-45a4-48d2-9c7f-73a90e6732d2"));
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Error,
            DiagnosticChannel.Diagnostic,
            "failed",
            correlationId,
            new Dictionary<string, string?>
            {
                ["reason"] = "denied",
            },
            DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        sink.Write(diagnosticEvent);

        Assert.Equal(
            "2025-01-02T03:04:05.0000000+00:00 [Error] " +
            "[9f2ea1a1-45a4-48d2-9c7f-73a90e6732d2] failed reason=denied" +
            Environment.NewLine,
            writer.ToString());
    }

    [Fact]
    public void WriteSkipsEventsBelowMinimumSeverity()
    {
        var writer = new StringWriter();
        var sink = new TextWriterDiagnosticSink(writer, DiagnosticSeverity.Warning);
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "ignored");

        sink.Write(diagnosticEvent);

        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public void ConstructorRejectsProtocolStdoutChannel()
    {
        var writer = new StringWriter();

        Assert.Throws<ArgumentOutOfRangeException>(
            "channel",
            () => new TextWriterDiagnosticSink(writer, channel: DiagnosticChannel.ProtocolStdout));
    }

    [Fact]
    public void WriteSkipsEventsForDifferentChannel()
    {
        var writer = new StringWriter();
        var sink = new TextWriterDiagnosticSink(writer, channel: DiagnosticChannel.HumanStdout);
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "diagnostic");

        sink.Write(diagnosticEvent);

        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public void WriteFormatsConfiguredHumanStdoutChannel()
    {
        var writer = new StringWriter();
        var sink = new TextWriterDiagnosticSink(writer, channel: DiagnosticChannel.HumanStdout);
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.HumanStdout,
            "human",
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        sink.Write(diagnosticEvent);

        Assert.Equal(
            "2025-01-02T03:04:05.0000000+00:00 [Information] human" + Environment.NewLine,
            writer.ToString());
    }
}
