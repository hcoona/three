using Hcoona.AzureAuth.CredProvider.Platform.Diagnostics;
using Xunit;

namespace Hcoona.AzureAuth.CredProvider.Platform.Tests;

public sealed class TextWriterDiagnosticSinkTests
{
    [Fact]
    public void WriteFormatsDiagnosticEvent()
    {
        var writer = new StringWriter { NewLine = "\n" };
        var sink = new TextWriterDiagnosticSink(writer);
        var timestamp = DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00");

        sink.Write(
            new DiagnosticEvent(
                DiagnosticSeverity.Warning,
                DiagnosticChannel.Diagnostic,
                "message",
                properties: new Dictionary<string, string?> { ["code"] = "Example" },
                timestamp: timestamp
            )
        );

        Assert.Equal(
            "2025-01-02T03:04:05.0000000+00:00 [Warning] message code=Example\n",
            writer.ToString()
        );
    }

    [Fact]
    public void WriteHonorsMinimumSeverity()
    {
        var writer = new StringWriter();
        var sink = new TextWriterDiagnosticSink(
            writer,
            minimumSeverity: DiagnosticSeverity.Warning
        );

        sink.Write(
            new DiagnosticEvent(
                DiagnosticSeverity.Information,
                DiagnosticChannel.Diagnostic,
                "ignored"
            )
        );

        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public void WriteHonorsConfiguredChannel()
    {
        var writer = new StringWriter();
        var sink = new TextWriterDiagnosticSink(writer, channel: DiagnosticChannel.HumanStdout);

        sink.Write(
            new DiagnosticEvent(
                DiagnosticSeverity.Information,
                DiagnosticChannel.Diagnostic,
                "ignored"
            )
        );
        sink.Write(
            new DiagnosticEvent(
                DiagnosticSeverity.Information,
                DiagnosticChannel.HumanStdout,
                "status"
            )
        );

        Assert.Contains("status", writer.ToString());
        Assert.DoesNotContain("ignored", writer.ToString());
    }

    [Fact]
    public void WriteSupportsUnicode()
    {
        var writer = new StringWriter();
        var sink = new TextWriterDiagnosticSink(writer);

        sink.Write(
            new DiagnosticEvent(
                DiagnosticSeverity.Error,
                DiagnosticChannel.Diagnostic,
                "diagnostic 🚀"
            )
        );

        Assert.Contains("diagnostic 🚀", writer.ToString());
    }

    [Fact]
    public void ConstructorRejectsProtocolStdout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TextWriterDiagnosticSink(
                new StringWriter(),
                channel: DiagnosticChannel.ProtocolStdout
            )
        );
    }
}
