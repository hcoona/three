using System.Text;
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
    public void WriteUsesConfiguredWriterNewLine()
    {
        var writer = new StringWriter
        {
            NewLine = "\r\n",
        };
        var sink = new TextWriterDiagnosticSink(writer);
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "configured newline",
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        sink.Write(diagnosticEvent);

        Assert.Equal(
            "2025-01-02T03:04:05.0000000+00:00 [Information] configured newline\r\n",
            writer.ToString());
    }

    [Fact]
    public void WriteSupportsNonBmpForExactStringWriter()
    {
        var writer = new StringWriter();
        var sink = new TextWriterDiagnosticSink(writer);
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "safe 🚀",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "ok 🧪",
            },
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        sink.Write(diagnosticEvent);

        Assert.Equal(
            "2025-01-02T03:04:05.0000000+00:00 [Information] safe 🚀 detail=ok 🧪"
                + Environment.NewLine,
            writer.ToString());
    }

    [Fact]
    public void WriteSupportsNonBmpForSynchronizedStringWriter()
    {
        var innerWriter = new StringWriter();
        TextWriter writer = TextWriter.Synchronized(innerWriter);
        var sink = new TextWriterDiagnosticSink(writer);
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "safe 🚀",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "ok 🧪",
            },
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        sink.Write(diagnosticEvent);

        Assert.Equal(
            "2025-01-02T03:04:05.0000000+00:00 [Information] safe 🚀 detail=ok 🧪"
                + Environment.NewLine,
            innerWriter.ToString());
    }

    [Fact]
    public void WriteWithCommitTrackingRejectsInvalidUtf16ForExactStringWriterBeforeOutput()
    {
        var writer = new StringWriter();
        ICommitTrackingDiagnosticSink sink = new TextWriterDiagnosticSink(writer);
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "\uD83Dleading invalid utf16",
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        DiagnosticWriteException exception = Assert.Throws<DiagnosticWriteException>(
            () => sink.WriteWithCommitTracking(diagnosticEvent));

        Assert.False(exception.OutputCommitted);
        Assert.IsType<EncoderFallbackException>(exception.OriginalException);
        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public void WriteWithCommitTrackingRejectsInvalidUtf16ForSynchronizedStringWriterBeforeOutput()
    {
        var innerWriter = new StringWriter();
        ICommitTrackingDiagnosticSink sink = new TextWriterDiagnosticSink(
            TextWriter.Synchronized(innerWriter));
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "\uD83Dleading invalid utf16",
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        DiagnosticWriteException exception = Assert.Throws<DiagnosticWriteException>(
            () => sink.WriteWithCommitTracking(diagnosticEvent));

        Assert.False(exception.OutputCommitted);
        Assert.IsType<EncoderFallbackException>(exception.OriginalException);
        Assert.Equal(string.Empty, innerWriter.ToString());
    }

    [Fact]
    public void WriteSupportsNonBmpForExactStreamWriter()
    {
        Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var stream = new MemoryStream();
        using var writer = new StreamWriter(
            stream,
            encoding,
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        var sink = new TextWriterDiagnosticSink(writer);
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "safe 🚀",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "ok 🧪",
            },
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        sink.Write(diagnosticEvent);

        writer.Flush();

        Assert.Equal(
            "2025-01-02T03:04:05.0000000+00:00 [Information] safe 🚀 detail=ok 🧪"
                + Environment.NewLine,
            encoding.GetString(stream.ToArray()));
    }

    [Fact]
    public void WriteFlushesAutoFlushStatefulExactStreamWriterBeforeManualFlush()
    {
        Encoding? maybeEncoding = TryCreateUtf7Encoding();
        Assert.SkipWhen(
            maybeEncoding is null,
            "UTF-7 encoding is unavailable or disabled on this target framework.");
        Encoding encoding = maybeEncoding!;
        Assert.Empty(encoding.GetPreamble());
        using var stream = new MemoryStream();
        using var writer = new StreamWriter(
            stream,
            encoding,
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = string.Empty,
        };
        var sink = new TextWriterDiagnosticSink(writer);
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "tail \u0100",
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));
        byte[] expectedBytes = encoding.GetBytes(
            "2025-01-02T03:04:05.0000000+00:00 [Information] tail \u0100");

        sink.Write(diagnosticEvent);

        Assert.Equal(expectedBytes, stream.ToArray());

        writer.Flush();

        Assert.Equal(expectedBytes, stream.ToArray());
    }

    [Fact]
    public void WriteWithCommitTrackingSupportsStringOnlyWriters()
    {
        var writer = new StringOnlyTextWriter();
        ICommitTrackingDiagnosticSink sink = new TextWriterDiagnosticSink(
            writer,
            DiagnosticSeverity.Warning);
        var correlationId = CorrelationId.FromGuid(
            Guid.Parse("9f2ea1a1-45a4-48d2-9c7f-73a90e6732d2"));
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Error,
            DiagnosticChannel.Diagnostic,
            "failed 🚀",
            correlationId,
            new Dictionary<string, string?>
            {
                ["reason"] = "denied 🧪",
            },
            DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        bool outputCommitted = sink.WriteWithCommitTracking(diagnosticEvent);

        Assert.True(outputCommitted);
        Assert.Equal(
            "2025-01-02T03:04:05.0000000+00:00 [Error] " +
            "[9f2ea1a1-45a4-48d2-9c7f-73a90e6732d2] failed 🚀 reason=denied 🧪" +
            Environment.NewLine,
            writer.Written);
        AssertWritesContainNoIsolatedSurrogates(writer.Writes);
    }

    [Fact]
    public void WriteWithCommitTrackingPreservesUnpairedSurrogatesInStringOnlyWriters()
    {
        var writer = new StringOnlyTextWriter();
        ICommitTrackingDiagnosticSink sink = new TextWriterDiagnosticSink(
            writer,
            channel: DiagnosticChannel.HumanStdout);
        const string message = "human \uD83D text \uDE80";
        DateTimeOffset timestamp = DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00");
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.HumanStdout,
            message,
            timestamp: timestamp);

        bool outputCommitted = sink.WriteWithCommitTracking(diagnosticEvent);

        Assert.True(outputCommitted);
        Assert.Equal(
            $"2025-01-02T03:04:05.0000000+00:00 [Information] {message}{Environment.NewLine}",
            writer.Written);
        Assert.Contains("\uD83D", writer.Writes);
        Assert.Contains("\uDE80", writer.Writes);
    }

    [Fact]
    public void WriteWithCommitTrackingReportsCommittedOutputForBmpStringWriteFailure()
    {
        var writer = new AppendThenThrowStringTextWriter(
            new IOException("diagnostic write failed"));
        ICommitTrackingDiagnosticSink sink = new TextWriterDiagnosticSink(writer);
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "safe message",
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        DiagnosticWriteException exception = Assert.Throws<DiagnosticWriteException>(
            () => sink.WriteWithCommitTracking(diagnosticEvent));

        Assert.True(exception.OutputCommitted);
        Assert.IsType<IOException>(exception.OriginalException);
        Assert.Equal("diagnostic write failed", exception.OriginalException.Message);
        Assert.Equal("2", writer.Written);
    }

    [Fact]
    public void WriteWithCommitTrackingReportsNoCommittedOutputForZeroByteProgressAwareFailure()
    {
        var writer = new ThrowingProgressAwareTextWriter(
            charsWrittenBeforeThrow: 0,
            exceptionToThrow: new IOException("diagnostic write failed"));
        ICommitTrackingDiagnosticSink sink = new TextWriterDiagnosticSink(writer);
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "safe message",
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        DiagnosticWriteException exception = Assert.Throws<DiagnosticWriteException>(
            () => sink.WriteWithCommitTracking(diagnosticEvent));

        Assert.False(exception.OutputCommitted);
        Assert.IsType<IOException>(exception.OriginalException);
        Assert.Equal("diagnostic write failed", exception.OriginalException.Message);
        Assert.Equal(string.Empty, writer.Written);
    }

    [Fact]
    public void WriteWithCommitTrackingReportsCommittedOutputForPartialProgressAwareFailure()
    {
        var writer = new ThrowingProgressAwareTextWriter(
            charsWrittenBeforeThrow: 1,
            exceptionToThrow: new IOException("diagnostic write failed"));
        ICommitTrackingDiagnosticSink sink = new TextWriterDiagnosticSink(writer);
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "safe message",
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        DiagnosticWriteException exception = Assert.Throws<DiagnosticWriteException>(
            () => sink.WriteWithCommitTracking(diagnosticEvent));

        Assert.True(exception.OutputCommitted);
        Assert.IsType<IOException>(exception.OriginalException);
        Assert.Equal("diagnostic write failed", exception.OriginalException.Message);
        Assert.Equal("2", writer.Written);
    }

    [Fact]
    public void WriteWithCommitTrackingReportsCommittedOutputForStringWriterSubclassFailure()
    {
        var writer = new ExternallyWritingThrowingStringWriter(
            new IOException("diagnostic write failed"));
        ICommitTrackingDiagnosticSink sink = new TextWriterDiagnosticSink(writer);
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "safe message",
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        DiagnosticWriteException exception = Assert.Throws<DiagnosticWriteException>(
            () => sink.WriteWithCommitTracking(diagnosticEvent));

        Assert.True(exception.OutputCommitted);
        Assert.IsType<IOException>(exception.OriginalException);
        Assert.Equal("diagnostic write failed", exception.OriginalException.Message);
        Assert.Equal("2", writer.Written);
        Assert.Equal(string.Empty, writer.ToString());
    }

    [Fact]
    public void WriteWithCommitTrackingSerializesConcurrentEventsWithoutLineInterleaving()
    {
        var writer = new CoordinatedProgressAwareTextWriter();
        ICommitTrackingDiagnosticSink sink = new TextWriterDiagnosticSink(writer);
        DateTimeOffset firstTimestamp = DateTimeOffset.Parse("1111-01-02T03:04:05.0000000+00:00");
        DateTimeOffset secondTimestamp = DateTimeOffset.Parse("9999-12-30T23:58:57.0000000+00:00");
        var firstEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "alpha 🚀",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "one 🧪",
            },
            timestamp: firstTimestamp);
        var secondEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "beta 🛰️",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "two 🪐",
            },
            timestamp: secondTimestamp);
        using var writersReady = new CountdownEvent(2);
        using var releaseWriters = new ManualResetEventSlim(false);
        bool? firstOutputCommitted = null;
        bool? secondOutputCommitted = null;
        Exception? firstException = null;
        Exception? secondException = null;

        var firstThread = new Thread(() =>
        {
            writersReady.Signal();
            releaseWriters.Wait();
            try
            {
                firstOutputCommitted = sink.WriteWithCommitTracking(firstEvent);
            }
            catch (Exception ex)
            {
                firstException = ex;
            }
        });
        var secondThread = new Thread(() =>
        {
            writersReady.Signal();
            releaseWriters.Wait();
            try
            {
                secondOutputCommitted = sink.WriteWithCommitTracking(secondEvent);
            }
            catch (Exception ex)
            {
                secondException = ex;
            }
        });

        firstThread.Start();
        secondThread.Start();
        Assert.True(
            writersReady.Wait(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken));
        releaseWriters.Set();

        Assert.True(writer.WaitForPendingWrite(TimeSpan.FromSeconds(10)));
        _ = writer.WaitForDistinctPendingThreadCount(2, TimeSpan.FromSeconds(1));

        DateTime releaseDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        int? lastReleasedThreadId = null;
        while (firstThread.IsAlive || secondThread.IsAlive)
        {
            if (!writer.WaitForPendingWrite(TimeSpan.FromMilliseconds(200)))
            {
                Assert.True(
                    DateTime.UtcNow < releaseDeadline,
                    "Timed out waiting for the next coordinated diagnostic chunk.");
                continue;
            }

            if (!writer.TryReleaseNextPendingChunk(lastReleasedThreadId, out int releasedThreadId))
            {
                continue;
            }

            lastReleasedThreadId = releasedThreadId;
        }

        Assert.True(firstThread.Join(TimeSpan.FromSeconds(10)));
        Assert.True(secondThread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(firstException);
        Assert.Null(secondException);
        Assert.True(firstOutputCommitted);
        Assert.True(secondOutputCommitted);

        string firstLine =
            "1111-01-02T03:04:05.0000000+00:00 [Information] alpha 🚀 detail=one 🧪"
            + Environment.NewLine;
        string secondLine =
            "9999-12-30T23:58:57.0000000+00:00 [Information] beta 🛰️ detail=two 🪐"
            + Environment.NewLine;
        string written = writer.Written;
        Assert.True(
            string.Equals(written, firstLine + secondLine, StringComparison.Ordinal)
                || string.Equals(written, secondLine + firstLine, StringComparison.Ordinal),
            $"Unexpected diagnostic output: '{written}'.");
        AssertWritesContainNoIsolatedSurrogates(writer.Writes);
    }

    [Fact]
    public void WriteWithCommitTrackingSerializesAcrossSeparateSinksSharingOneWriter()
    {
        var writer = new CoordinatedProgressAwareTextWriter();
        ICommitTrackingDiagnosticSink firstSink = new TextWriterDiagnosticSink(writer);
        ICommitTrackingDiagnosticSink secondSink = new TextWriterDiagnosticSink(writer);
        DateTimeOffset firstTimestamp = DateTimeOffset.Parse("1111-01-02T03:04:05.0000000+00:00");
        DateTimeOffset secondTimestamp = DateTimeOffset.Parse("9999-12-30T23:58:57.0000000+00:00");
        var firstEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "alpha 🚀",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "one 🧪",
            },
            timestamp: firstTimestamp);
        var secondEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "beta 🛰️",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "two 🪐",
            },
            timestamp: secondTimestamp);
        using var writersReady = new CountdownEvent(2);
        using var releaseWriters = new ManualResetEventSlim(false);
        bool? firstOutputCommitted = null;
        bool? secondOutputCommitted = null;
        Exception? firstException = null;
        Exception? secondException = null;

        var firstThread = new Thread(() =>
        {
            writersReady.Signal();
            releaseWriters.Wait();
            try
            {
                firstOutputCommitted = firstSink.WriteWithCommitTracking(firstEvent);
            }
            catch (Exception ex)
            {
                firstException = ex;
            }
        });
        var secondThread = new Thread(() =>
        {
            writersReady.Signal();
            releaseWriters.Wait();
            try
            {
                secondOutputCommitted = secondSink.WriteWithCommitTracking(secondEvent);
            }
            catch (Exception ex)
            {
                secondException = ex;
            }
        });

        firstThread.Start();
        secondThread.Start();
        Assert.True(
            writersReady.Wait(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken));
        releaseWriters.Set();

        bool observedTwoPendingThreads = writer.WaitForDistinctPendingThreadCount(
            minimumDistinctPendingThreadCount: 2,
            TimeSpan.FromMilliseconds(500));

        DateTime releaseDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        int? lastReleasedThreadId = null;
        while (firstThread.IsAlive || secondThread.IsAlive)
        {
            if (!writer.WaitForPendingWrite(TimeSpan.FromMilliseconds(200)))
            {
                Assert.True(
                    DateTime.UtcNow < releaseDeadline,
                    "Timed out waiting for the next coordinated diagnostic chunk.");
                continue;
            }

            if (!writer.TryReleaseNextPendingChunk(lastReleasedThreadId, out int releasedThreadId))
            {
                continue;
            }

            lastReleasedThreadId = releasedThreadId;
        }

        Assert.True(firstThread.Join(TimeSpan.FromSeconds(10)));
        Assert.True(secondThread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(firstException);
        Assert.Null(secondException);
        Assert.True(firstOutputCommitted);
        Assert.True(secondOutputCommitted);
        Assert.False(
            observedTwoPendingThreads,
            "Separate sinks sharing one writer should share one writer-scoped lock.");

        string firstLine =
            "1111-01-02T03:04:05.0000000+00:00 [Information] alpha 🚀 detail=one 🧪"
            + Environment.NewLine;
        string secondLine =
            "9999-12-30T23:58:57.0000000+00:00 [Information] beta 🛰️ detail=two 🪐"
            + Environment.NewLine;
        string written = writer.Written;
        Assert.True(
            string.Equals(written, firstLine + secondLine, StringComparison.Ordinal)
                || string.Equals(written, secondLine + firstLine, StringComparison.Ordinal),
            $"Unexpected diagnostic output: '{written}'.");
        AssertWritesContainNoIsolatedSurrogates(writer.Writes);
    }

    [Fact]
    public void WriteWithCommitTrackingSerializesAcrossSeparateSinksSharingStringBuilder()
    {
        var sharedBuilder = new StringBuilder();
        var writeCoordinator = new CoordinatedSharedStringWriterCoordinator();
        ICommitTrackingDiagnosticSink firstSink = new TextWriterDiagnosticSink(
            new CoordinatedSharedStringWriter(sharedBuilder, writeCoordinator));
        ICommitTrackingDiagnosticSink secondSink = new TextWriterDiagnosticSink(
            new CoordinatedSharedStringWriter(sharedBuilder, writeCoordinator));
        DateTimeOffset firstTimestamp = DateTimeOffset.Parse("1111-01-02T03:04:05.0000000+00:00");
        DateTimeOffset secondTimestamp = DateTimeOffset.Parse("9999-12-30T23:58:57.0000000+00:00");
        var firstEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "alpha line",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "one",
            },
            timestamp: firstTimestamp);
        var secondEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "beta line",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "two",
            },
            timestamp: secondTimestamp);
        using var writersReady = new CountdownEvent(2);
        using var releaseWriters = new ManualResetEventSlim(false);
        bool? firstOutputCommitted = null;
        bool? secondOutputCommitted = null;
        Exception? firstException = null;
        Exception? secondException = null;

        var firstThread = new Thread(() =>
        {
            writersReady.Signal();
            releaseWriters.Wait();
            try
            {
                firstOutputCommitted = firstSink.WriteWithCommitTracking(firstEvent);
            }
            catch (Exception ex)
            {
                firstException = ex;
            }
        });
        var secondThread = new Thread(() =>
        {
            writersReady.Signal();
            releaseWriters.Wait();
            try
            {
                secondOutputCommitted = secondSink.WriteWithCommitTracking(secondEvent);
            }
            catch (Exception ex)
            {
                secondException = ex;
            }
        });

        firstThread.Start();
        secondThread.Start();
        Assert.True(
            writersReady.Wait(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken));
        releaseWriters.Set();

        Assert.True(writeCoordinator.WaitForPendingWrite(TimeSpan.FromSeconds(10)));
        bool observedTwoPendingThreads = writeCoordinator.WaitForDistinctPendingThreadCount(
            minimumDistinctPendingThreadCount: 2,
            TimeSpan.FromMilliseconds(500));

        DateTime releaseDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        int? lastReleasedThreadId = null;
        while (firstThread.IsAlive || secondThread.IsAlive)
        {
            if (!writeCoordinator.WaitForPendingWrite(TimeSpan.FromMilliseconds(200)))
            {
                Assert.True(
                    DateTime.UtcNow < releaseDeadline,
                    "Timed out waiting for the next shared StringWriter diagnostic chunk.");
                continue;
            }

            if (
                !writeCoordinator.TryReleaseNextPendingWrite(
                    lastReleasedThreadId,
                    out int releasedThreadId)
            )
            {
                continue;
            }

            lastReleasedThreadId = releasedThreadId;
        }

        Assert.True(firstThread.Join(TimeSpan.FromSeconds(10)));
        Assert.True(secondThread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(firstException);
        Assert.Null(secondException);
        Assert.True(firstOutputCommitted);
        Assert.True(secondOutputCommitted);
        Assert.False(
            observedTwoPendingThreads,
            "Separate StringWriter wrappers sharing one StringBuilder should share one "
                + "builder-scoped lock.");

        string firstLine =
            "1111-01-02T03:04:05.0000000+00:00 [Information] alpha line detail=one"
            + Environment.NewLine;
        string secondLine =
            "9999-12-30T23:58:57.0000000+00:00 [Information] beta line detail=two"
            + Environment.NewLine;
        string written = sharedBuilder.ToString();
        Assert.True(
            string.Equals(written, firstLine + secondLine, StringComparison.Ordinal)
                || string.Equals(written, secondLine + firstLine, StringComparison.Ordinal),
            $"Unexpected diagnostic output: '{written}'.");
    }

    [Fact]
    public void
        WriteWithCommitTrackingSerializesAcrossSeparateSinksSharingSynchronizedStringWriters()
    {
        var sharedBuilder = new StringBuilder();
        var writeCoordinator = new CoordinatedSharedStringWriterCoordinator();
        ICommitTrackingDiagnosticSink firstSink = new TextWriterDiagnosticSink(
            TextWriter.Synchronized(
                new CoordinatedSharedStringWriter(sharedBuilder, writeCoordinator)));
        ICommitTrackingDiagnosticSink secondSink = new TextWriterDiagnosticSink(
            TextWriter.Synchronized(
                new CoordinatedSharedStringWriter(sharedBuilder, writeCoordinator)));
        DateTimeOffset firstTimestamp = DateTimeOffset.Parse("1111-01-02T03:04:05.0000000+00:00");
        DateTimeOffset secondTimestamp = DateTimeOffset.Parse("9999-12-30T23:58:57.0000000+00:00");
        var firstEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "alpha line",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "one",
            },
            timestamp: firstTimestamp);
        var secondEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "beta line",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "two",
            },
            timestamp: secondTimestamp);
        using var writersReady = new CountdownEvent(2);
        using var releaseWriters = new ManualResetEventSlim(false);
        bool? firstOutputCommitted = null;
        bool? secondOutputCommitted = null;
        Exception? firstException = null;
        Exception? secondException = null;

        var firstThread = new Thread(() =>
        {
            writersReady.Signal();
            releaseWriters.Wait();
            try
            {
                firstOutputCommitted = firstSink.WriteWithCommitTracking(firstEvent);
            }
            catch (Exception ex)
            {
                firstException = ex;
            }
        });
        var secondThread = new Thread(() =>
        {
            writersReady.Signal();
            releaseWriters.Wait();
            try
            {
                secondOutputCommitted = secondSink.WriteWithCommitTracking(secondEvent);
            }
            catch (Exception ex)
            {
                secondException = ex;
            }
        });

        firstThread.Start();
        secondThread.Start();
        Assert.True(
            writersReady.Wait(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken));
        releaseWriters.Set();

        Assert.True(writeCoordinator.WaitForPendingWrite(TimeSpan.FromSeconds(10)));
        bool observedTwoPendingThreads = writeCoordinator.WaitForDistinctPendingThreadCount(
            minimumDistinctPendingThreadCount: 2,
            TimeSpan.FromMilliseconds(500));

        DateTime releaseDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        int? lastReleasedThreadId = null;
        while (firstThread.IsAlive || secondThread.IsAlive)
        {
            if (!writeCoordinator.WaitForPendingWrite(TimeSpan.FromMilliseconds(200)))
            {
                Assert.True(
                    DateTime.UtcNow < releaseDeadline,
                    "Timed out waiting for the next shared synchronized StringWriter "
                        + "diagnostic chunk.");
                continue;
            }

            if (
                !writeCoordinator.TryReleaseNextPendingWrite(
                    lastReleasedThreadId,
                    out int releasedThreadId)
            )
            {
                continue;
            }

            lastReleasedThreadId = releasedThreadId;
        }

        Assert.True(firstThread.Join(TimeSpan.FromSeconds(10)));
        Assert.True(secondThread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(firstException);
        Assert.Null(secondException);
        Assert.True(firstOutputCommitted);
        Assert.True(secondOutputCommitted);
        Assert.False(
            observedTwoPendingThreads,
            "Separate synchronized StringWriter wrappers sharing one StringBuilder "
                + "should share one builder-scoped lock.");

        string firstLine =
            "1111-01-02T03:04:05.0000000+00:00 [Information] alpha line detail=one"
            + Environment.NewLine;
        string secondLine =
            "9999-12-30T23:58:57.0000000+00:00 [Information] beta line detail=two"
            + Environment.NewLine;
        string written = sharedBuilder.ToString();
        Assert.True(
            string.Equals(written, firstLine + secondLine, StringComparison.Ordinal)
                || string.Equals(written, secondLine + firstLine, StringComparison.Ordinal),
            $"Unexpected diagnostic output: '{written}'.");
    }

    [Fact]
    public void
        WriteWithCommitTrackingSerializesWithExternalWritesThroughSameSynchronizedStringWriter()
    {
        var sharedBuilder = new StringBuilder();
        var writeCoordinator = new CoordinatedSharedStringWriterCoordinator();
        TextWriter writer = TextWriter.Synchronized(
            new CoordinatedSharedStringWriter(sharedBuilder, writeCoordinator));
        ICommitTrackingDiagnosticSink sink = new TextWriterDiagnosticSink(writer);
        DateTimeOffset timestamp = DateTimeOffset.Parse("1111-01-02T03:04:05.0000000+00:00");
        const string externalWrite = "external note";
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "alpha line",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "one",
            },
            timestamp: timestamp);
        using var writersReady = new CountdownEvent(2);
        using var releaseWriters = new ManualResetEventSlim(false);
        bool? outputCommitted = null;
        Exception? sinkException = null;
        Exception? externalException = null;

        var sinkThread = new Thread(() =>
        {
            writersReady.Signal();
            releaseWriters.Wait();
            try
            {
                outputCommitted = sink.WriteWithCommitTracking(diagnosticEvent);
            }
            catch (Exception ex)
            {
                sinkException = ex;
            }
        });
        var externalThread = new Thread(() =>
        {
            writersReady.Signal();
            releaseWriters.Wait();
            try
            {
                writer.Write(externalWrite);
            }
            catch (Exception ex)
            {
                externalException = ex;
            }
        });

        sinkThread.Start();
        externalThread.Start();
        Assert.True(
            writersReady.Wait(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken));
        releaseWriters.Set();

        Assert.True(writeCoordinator.WaitForPendingWrite(TimeSpan.FromSeconds(10)));
        bool observedTwoPendingThreads = writeCoordinator.WaitForDistinctPendingThreadCount(
            minimumDistinctPendingThreadCount: 2,
            TimeSpan.FromMilliseconds(500));

        DateTime releaseDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        int? lastReleasedThreadId = null;
        while (sinkThread.IsAlive || externalThread.IsAlive)
        {
            if (!writeCoordinator.WaitForPendingWrite(TimeSpan.FromMilliseconds(200)))
            {
                Assert.True(
                    DateTime.UtcNow < releaseDeadline,
                    "Timed out waiting for the next same-wrapper synchronized StringWriter "
                        + "write.");
                continue;
            }

            if (
                !writeCoordinator.TryReleaseNextPendingWrite(
                    lastReleasedThreadId,
                    out int releasedThreadId)
            )
            {
                continue;
            }

            lastReleasedThreadId = releasedThreadId;
        }

        Assert.True(sinkThread.Join(TimeSpan.FromSeconds(10)));
        Assert.True(externalThread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(sinkException);
        Assert.Null(externalException);
        Assert.True(outputCommitted);
        Assert.False(
            observedTwoPendingThreads,
            "The same synchronized StringWriter instance should keep its wrapper monitor while "
                + "diagnostics bypass the wrapper.");

        string diagnosticLine =
            "1111-01-02T03:04:05.0000000+00:00 [Information] alpha line detail=one"
            + Environment.NewLine;
        string written = sharedBuilder.ToString();
        Assert.True(
            string.Equals(written, diagnosticLine + externalWrite, StringComparison.Ordinal)
                || string.Equals(written, externalWrite + diagnosticLine, StringComparison.Ordinal),
            $"Unexpected diagnostic output: '{written}'.");
    }

    [Fact]
    public void WriteWithCommitTrackingSerializesAcrossSeparateSinksSharingAutoFlushStreamWriter()
    {
        using var stream = new CoordinatedWriteStream(
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using var firstWriter = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1,
            leaveOpen: true)
        {
            AutoFlush = true,
        };
        using var secondWriter = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1,
            leaveOpen: true)
        {
            AutoFlush = true,
        };
        ICommitTrackingDiagnosticSink firstSink = new TextWriterDiagnosticSink(firstWriter);
        ICommitTrackingDiagnosticSink secondSink = new TextWriterDiagnosticSink(secondWriter);
        DateTimeOffset firstTimestamp = DateTimeOffset.Parse("1111-01-02T03:04:05.0000000+00:00");
        DateTimeOffset secondTimestamp = DateTimeOffset.Parse("9999-12-30T23:58:57.0000000+00:00");
        var firstEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "alpha line",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "one",
            },
            timestamp: firstTimestamp);
        var secondEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "beta line",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "two",
            },
            timestamp: secondTimestamp);
        using var writersReady = new CountdownEvent(2);
        using var releaseWriters = new ManualResetEventSlim(false);
        bool? firstOutputCommitted = null;
        bool? secondOutputCommitted = null;
        Exception? firstException = null;
        Exception? secondException = null;

        var firstThread = new Thread(() =>
        {
            writersReady.Signal();
            releaseWriters.Wait();
            try
            {
                firstOutputCommitted = firstSink.WriteWithCommitTracking(firstEvent);
            }
            catch (Exception ex)
            {
                firstException = ex;
            }
        });
        var secondThread = new Thread(() =>
        {
            writersReady.Signal();
            releaseWriters.Wait();
            try
            {
                secondOutputCommitted = secondSink.WriteWithCommitTracking(secondEvent);
            }
            catch (Exception ex)
            {
                secondException = ex;
            }
        });

        firstThread.Start();
        secondThread.Start();
        Assert.True(
            writersReady.Wait(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken));
        releaseWriters.Set();

        Assert.True(stream.WaitForPendingWrite(TimeSpan.FromSeconds(10)));
        bool observedTwoPendingThreads = stream.WaitForDistinctPendingThreadCount(
            minimumDistinctPendingThreadCount: 2,
            TimeSpan.FromMilliseconds(500));

        DateTime releaseDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        int? lastReleasedThreadId = null;
        while (firstThread.IsAlive || secondThread.IsAlive)
        {
            if (!stream.WaitForPendingWrite(TimeSpan.FromMilliseconds(200)))
            {
                Assert.True(
                    DateTime.UtcNow < releaseDeadline,
                    "Timed out waiting for the next shared StreamWriter diagnostic chunk.");
                continue;
            }

            if (!stream.TryReleaseNextPendingWrite(lastReleasedThreadId, out int releasedThreadId))
            {
                continue;
            }

            lastReleasedThreadId = releasedThreadId;
        }

        Assert.True(firstThread.Join(TimeSpan.FromSeconds(10)));
        Assert.True(secondThread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(firstException);
        Assert.Null(secondException);
        Assert.True(firstOutputCommitted);
        Assert.True(secondOutputCommitted);
        Assert.False(
            observedTwoPendingThreads,
            "Separate StreamWriter wrappers sharing one stream should share one "
                + "stream-scoped lock.");

        string firstLine =
            "1111-01-02T03:04:05.0000000+00:00 [Information] alpha line detail=one"
            + Environment.NewLine;
        string secondLine =
            "9999-12-30T23:58:57.0000000+00:00 [Information] beta line detail=two"
            + Environment.NewLine;
        string written = stream.Written;
        Assert.True(
            string.Equals(written, firstLine + secondLine, StringComparison.Ordinal)
                || string.Equals(written, secondLine + firstLine, StringComparison.Ordinal),
            $"Unexpected diagnostic output: '{written}'.");
    }

    [Fact]
    public void WriteWithCommitTrackingFlushesBufferedSharedStreamWriterBeforeReturning()
    {
        Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var stream = new MemoryStream();
        using var firstWriter = new StreamWriter(
            stream,
            encoding,
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        using var secondWriter = new StreamWriter(
            stream,
            encoding,
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        ICommitTrackingDiagnosticSink firstSink = new TextWriterDiagnosticSink(firstWriter);
        ICommitTrackingDiagnosticSink secondSink = new TextWriterDiagnosticSink(secondWriter);
        DateTimeOffset firstTimestamp = DateTimeOffset.Parse("1111-01-02T03:04:05.0000000+00:00");
        DateTimeOffset secondTimestamp = DateTimeOffset.Parse("9999-12-30T23:58:57.0000000+00:00");
        var firstEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "alpha line",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "one",
            },
            timestamp: firstTimestamp);
        var secondEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "beta line",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "two",
            },
            timestamp: secondTimestamp);

        Assert.True(firstSink.WriteWithCommitTracking(firstEvent));
        Assert.True(secondSink.WriteWithCommitTracking(secondEvent));

        string firstLine =
            "1111-01-02T03:04:05.0000000+00:00 [Information] alpha line detail=one"
            + Environment.NewLine;
        string secondLine =
            "9999-12-30T23:58:57.0000000+00:00 [Information] beta line detail=two"
            + Environment.NewLine;

        Assert.Equal(firstLine + secondLine, encoding.GetString(stream.ToArray()));

        secondWriter.Flush();
        firstWriter.Flush();

        Assert.Equal(firstLine + secondLine, encoding.GetString(stream.ToArray()));
    }

    [Fact]
    public void WriteWithCommitTrackingFlushesBufferedStandardConsoleTextWriterBeforeReturning()
    {
        Encoding encoding = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);
        using var downstreamStream = new MemoryStream();
        using var bufferedStream = new BufferedStream(downstreamStream, bufferSize: 1024);
        var writer = new StandardConsoleTextWriter(
            bufferedStream,
            encoding,
            Environment.NewLine);
        ICommitTrackingDiagnosticSink sink = new TextWriterDiagnosticSink(writer);
        DateTimeOffset timestamp = DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00");
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "safe message",
            timestamp: timestamp);

        Assert.True(sink.WriteWithCommitTracking(diagnosticEvent));

        string expectedLine =
            "2025-01-02T03:04:05.0000000+00:00 [Information] safe message"
            + Environment.NewLine;
        Assert.Equal(expectedLine, encoding.GetString(downstreamStream.ToArray()));

        writer.Flush();

        Assert.Equal(expectedLine, encoding.GetString(downstreamStream.ToArray()));
    }

    [Fact]
    public void
        WriteWithCommitTrackingRejectsPreambleEncodingForSharedExactStreamWritersBeforeOutput()
    {
        Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        Assert.NotEmpty(encoding.GetPreamble());
        using var stream = new MemoryStream();
        using var firstWriter = new StreamWriter(
            stream,
            encoding,
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        using var secondWriter = new StreamWriter(
            stream,
            encoding,
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        ICommitTrackingDiagnosticSink firstSink = new TextWriterDiagnosticSink(firstWriter);
        ICommitTrackingDiagnosticSink secondSink = new TextWriterDiagnosticSink(secondWriter);
        var firstEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "alpha line",
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));
        var secondEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "beta line",
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:06.0000000+00:00"));

        DiagnosticWriteException firstException = Assert.Throws<DiagnosticWriteException>(
            () => firstSink.WriteWithCommitTracking(firstEvent));
        DiagnosticWriteException secondException = Assert.Throws<DiagnosticWriteException>(
            () => secondSink.WriteWithCommitTracking(secondEvent));

        Assert.False(firstException.OutputCommitted);
        Assert.False(secondException.OutputCommitted);
        Assert.IsType<NotSupportedException>(firstException.OriginalException);
        Assert.IsType<NotSupportedException>(secondException.OriginalException);
        Assert.Equal(0, stream.Length);
        Assert.Empty(stream.ToArray());
    }

    [Fact]
    public void
        WriteWithCommitTrackingSerializesSharedBufferedSynchronizedStreamWriters()
    {
        using var stream = new CoordinatedWriteStream(
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using var firstWriter = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        using var secondWriter = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        ICommitTrackingDiagnosticSink firstSink = new TextWriterDiagnosticSink(
            TextWriter.Synchronized(firstWriter));
        ICommitTrackingDiagnosticSink secondSink = new TextWriterDiagnosticSink(
            TextWriter.Synchronized(secondWriter));
        DateTimeOffset firstTimestamp = DateTimeOffset.Parse("1111-01-02T03:04:05.0000000+00:00");
        DateTimeOffset secondTimestamp = DateTimeOffset.Parse("9999-12-30T23:58:57.0000000+00:00");
        var firstEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "alpha line",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "one",
            },
            timestamp: firstTimestamp);
        var secondEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "beta line",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "two",
            },
            timestamp: secondTimestamp);
        using var writersReady = new CountdownEvent(2);
        using var releaseWriters = new ManualResetEventSlim(false);
        bool? firstOutputCommitted = null;
        bool? secondOutputCommitted = null;
        Exception? firstException = null;
        Exception? secondException = null;

        var firstThread = new Thread(() =>
        {
            writersReady.Signal();
            releaseWriters.Wait();
            try
            {
                firstOutputCommitted = firstSink.WriteWithCommitTracking(firstEvent);
            }
            catch (Exception ex)
            {
                firstException = ex;
            }
        });
        var secondThread = new Thread(() =>
        {
            writersReady.Signal();
            releaseWriters.Wait();
            try
            {
                secondOutputCommitted = secondSink.WriteWithCommitTracking(secondEvent);
            }
            catch (Exception ex)
            {
                secondException = ex;
            }
        });

        firstThread.Start();
        secondThread.Start();
        Assert.True(
            writersReady.Wait(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken));
        releaseWriters.Set();

        Assert.True(stream.WaitForPendingWrite(TimeSpan.FromSeconds(10)));
        bool observedTwoPendingThreads = stream.WaitForDistinctPendingThreadCount(
            minimumDistinctPendingThreadCount: 2,
            TimeSpan.FromMilliseconds(500));

        DateTime releaseDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        int? lastReleasedThreadId = null;
        while (firstThread.IsAlive || secondThread.IsAlive)
        {
            if (!stream.WaitForPendingWrite(TimeSpan.FromMilliseconds(200)))
            {
                Assert.True(
                    DateTime.UtcNow < releaseDeadline,
                    "Timed out waiting for the next shared synchronized StreamWriter "
                        + "diagnostic chunk.");
                continue;
            }

            if (!stream.TryReleaseNextPendingWrite(lastReleasedThreadId, out int releasedThreadId))
            {
                continue;
            }

            lastReleasedThreadId = releasedThreadId;
        }

        Assert.True(firstThread.Join(TimeSpan.FromSeconds(10)));
        Assert.True(secondThread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(firstException);
        Assert.Null(secondException);
        Assert.True(firstOutputCommitted);
        Assert.True(secondOutputCommitted);
        Assert.False(
            observedTwoPendingThreads,
            "Separate synchronized StreamWriter wrappers sharing one stream should share "
                + "one stream-scoped lock.");

        string firstLine =
            "1111-01-02T03:04:05.0000000+00:00 [Information] alpha line detail=one"
            + Environment.NewLine;
        string secondLine =
            "9999-12-30T23:58:57.0000000+00:00 [Information] beta line detail=two"
            + Environment.NewLine;
        string written = stream.Written;
        Assert.True(
            string.Equals(written, firstLine + secondLine, StringComparison.Ordinal)
                || string.Equals(written, secondLine + firstLine, StringComparison.Ordinal),
            $"Unexpected diagnostic output: '{written}'.");
    }

    [Fact]
    public void
        WriteWithCommitTrackingSerializesWithExternalWritesThroughSameSynchronizedStreamWriter()
    {
        using var stream = new CoordinatedWriteStream(
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        using var innerWriter = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1,
            leaveOpen: true)
        {
            AutoFlush = true,
        };
        TextWriter writer = TextWriter.Synchronized(innerWriter);
        ICommitTrackingDiagnosticSink sink = new TextWriterDiagnosticSink(writer);
        DateTimeOffset timestamp = DateTimeOffset.Parse("1111-01-02T03:04:05.0000000+00:00");
        const string externalWrite = "external note";
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "alpha line",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "one",
            },
            timestamp: timestamp);
        using var writersReady = new CountdownEvent(2);
        using var releaseWriters = new ManualResetEventSlim(false);
        bool? outputCommitted = null;
        Exception? sinkException = null;
        Exception? externalException = null;

        var sinkThread = new Thread(() =>
        {
            writersReady.Signal();
            releaseWriters.Wait();
            try
            {
                outputCommitted = sink.WriteWithCommitTracking(diagnosticEvent);
            }
            catch (Exception ex)
            {
                sinkException = ex;
            }
        });
        var externalThread = new Thread(() =>
        {
            writersReady.Signal();
            releaseWriters.Wait();
            try
            {
                writer.Write(externalWrite);
            }
            catch (Exception ex)
            {
                externalException = ex;
            }
        });

        sinkThread.Start();
        externalThread.Start();
        Assert.True(
            writersReady.Wait(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken));
        releaseWriters.Set();

        Assert.True(stream.WaitForPendingWrite(TimeSpan.FromSeconds(10)));
        bool observedTwoPendingThreads = stream.WaitForDistinctPendingThreadCount(
            minimumDistinctPendingThreadCount: 2,
            TimeSpan.FromMilliseconds(500));

        DateTime releaseDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        int? lastReleasedThreadId = null;
        while (sinkThread.IsAlive || externalThread.IsAlive)
        {
            if (!stream.WaitForPendingWrite(TimeSpan.FromMilliseconds(200)))
            {
                Assert.True(
                    DateTime.UtcNow < releaseDeadline,
                    "Timed out waiting for the next same-wrapper synchronized StreamWriter "
                        + "write.");
                continue;
            }

            if (!stream.TryReleaseNextPendingWrite(lastReleasedThreadId, out int releasedThreadId))
            {
                continue;
            }

            lastReleasedThreadId = releasedThreadId;
        }

        Assert.True(sinkThread.Join(TimeSpan.FromSeconds(10)));
        Assert.True(externalThread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(sinkException);
        Assert.Null(externalException);
        Assert.True(outputCommitted);
        Assert.False(
            observedTwoPendingThreads,
            "The same synchronized StreamWriter instance should keep its wrapper monitor "
                + "while diagnostics bypass the wrapper.");

        string diagnosticLine =
            "1111-01-02T03:04:05.0000000+00:00 [Information] alpha line detail=one"
            + Environment.NewLine;
        string written = stream.Written;
        Assert.True(
            string.Equals(written, diagnosticLine + externalWrite, StringComparison.Ordinal)
                || string.Equals(written, externalWrite + diagnosticLine, StringComparison.Ordinal),
            $"Unexpected diagnostic output: '{written}'.");
    }

    [Fact]
    public void WriteWithCommitTrackingRejectsNonBmpForCharOnlyWriters()
    {
        var writer = new CharOnlyTextWriter();
        ICommitTrackingDiagnosticSink sink = new TextWriterDiagnosticSink(
            writer,
            DiagnosticSeverity.Warning);
        var correlationId = CorrelationId.FromGuid(
            Guid.Parse("9f2ea1a1-45a4-48d2-9c7f-73a90e6732d2"));
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Error,
            DiagnosticChannel.Diagnostic,
            "failed 🚀",
            correlationId,
            new Dictionary<string, string?>
            {
                ["reason"] = "denied 🧪",
            },
            DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        DiagnosticWriteException exception = Assert.Throws<DiagnosticWriteException>(
            () => sink.WriteWithCommitTracking(diagnosticEvent));

        Assert.False(exception.OutputCommitted);
        Assert.IsType<NotSupportedException>(exception.OriginalException);
        Assert.Equal(string.Empty, writer.Written);
    }

    [Fact]
    public void WriteWithCommitTrackingRejectsNonBmpForSpanOnlyWriters()
    {
        var writer = new SpanOnlyTextWriter();
        ICommitTrackingDiagnosticSink sink = new TextWriterDiagnosticSink(
            writer,
            DiagnosticSeverity.Warning);
        var correlationId = CorrelationId.FromGuid(
            Guid.Parse("9f2ea1a1-45a4-48d2-9c7f-73a90e6732d2"));
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Error,
            DiagnosticChannel.Diagnostic,
            "failed 🚀",
            correlationId,
            new Dictionary<string, string?>
            {
                ["reason"] = "denied 🧪",
            },
            DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        DiagnosticWriteException exception = Assert.Throws<DiagnosticWriteException>(
            () => sink.WriteWithCommitTracking(diagnosticEvent));

        Assert.False(exception.OutputCommitted);
        Assert.IsType<NotSupportedException>(exception.OriginalException);
        Assert.Equal(string.Empty, writer.Written);
    }

    [Fact]
    public void WriteRejectsNonBmpForForwardingStringWriterBeforeOutput()
    {
        var writer = new ForwardingStringTextWriter();
        var sink = new TextWriterDiagnosticSink(writer);
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "failed 🚀",
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        Assert.Throws<NotSupportedException>(() => sink.Write(diagnosticEvent));

        Assert.Equal(string.Empty, writer.Written);
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

    [Fact]
    public void StandardConsoleWritersUseTrustedEncodingAndNewLine()
    {
        TextWriter originalOut = Console.Out;
        TextWriter originalError = Console.Error;
        Encoding originalOutputEncoding = Console.OutputEncoding;

        try
        {
            Console.OutputEncoding = Encoding.Latin1;

            var overriddenOut = new StringWriter
            {
                NewLine = "\r\r\n",
            };
            var overriddenError = new StringWriter
            {
                NewLine = "\n\r",
            };

            Console.SetOut(overriddenOut);
            Console.SetError(overriddenError);

            StandardConsoleTextWriter standardOutput = StandardConsoleTextWriter.StandardOutput();
            StandardConsoleTextWriter standardError = StandardConsoleTextWriter.StandardError();

            Assert.Equal(Environment.NewLine, standardOutput.NewLine);
            Assert.Equal(Environment.NewLine, standardError.NewLine);
            Assert.Equal(Encoding.UTF8.WebName, standardOutput.Encoding.WebName);
            Assert.Equal(Encoding.UTF8.WebName, standardError.Encoding.WebName);
            Assert.IsType<UTF8Encoding>(standardOutput.Encoding);
            Assert.IsType<UTF8Encoding>(standardError.Encoding);
            Assert.Empty(standardOutput.Encoding.GetPreamble());
            Assert.Empty(standardError.Encoding.GetPreamble());
            Assert.IsType<EncoderExceptionFallback>(standardOutput.Encoding.EncoderFallback);
            Assert.IsType<EncoderExceptionFallback>(standardError.Encoding.EncoderFallback);
            Assert.IsType<DecoderExceptionFallback>(standardOutput.Encoding.DecoderFallback);
            Assert.IsType<DecoderExceptionFallback>(standardError.Encoding.DecoderFallback);
            Assert.NotEqual(overriddenOut.NewLine, standardOutput.NewLine);
            Assert.NotEqual(overriddenError.NewLine, standardError.NewLine);
            Assert.NotEqual(Console.OutputEncoding.WebName, standardOutput.Encoding.WebName);
            Assert.NotEqual(Console.OutputEncoding.WebName, standardError.Encoding.WebName);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            Console.OutputEncoding = originalOutputEncoding;
        }
    }

    [Fact]
    public void StandardConsoleTextWriterRejectsNonStrictUtf8Encoding()
    {
        using var stream = new MemoryStream();

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new StandardConsoleTextWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                Environment.NewLine));

        Assert.Equal("encoding", exception.ParamName);
    }

    [Fact]
    public void StandardConsoleTextWriterRejectsNonUtf8Encoding()
    {
        using var stream = new MemoryStream();

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            new StandardConsoleTextWriter(
                stream,
                Encoding.Latin1,
                Environment.NewLine));

        Assert.Equal("encoding", exception.ParamName);
    }

    [Fact]
    public void
        StandardConsoleTextWriterWriteWithProgressReportsZeroCharsForIsolatedSurrogateFailure()
    {
        using var stream = new MemoryStream();
        var writer = new StandardConsoleTextWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            Environment.NewLine);
        int charsWritten = 0;

        Assert.Throws<EncoderFallbackException>(
            () => writer.WriteWithProgress("\uD83D".AsSpan(), ref charsWritten));

        Assert.Equal(0, charsWritten);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void
        UnicodeScalarWriterRejectsTrailingInvalidUtf16ForStandardConsoleTextWriterBeforeOutput()
    {
        using var stream = new MemoryStream();
        TextWriter writer = new StandardConsoleTextWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            Environment.NewLine);
        bool outputCommitted = false;

        Assert.Throws<EncoderFallbackException>(() => TextWriterUnicodeScalarWriter.Write(
            writer,
            "abc\uD83D",
            ref outputCommitted,
            trackCommit: true));

        Assert.False(outputCommitted);
        Assert.Equal(0, stream.Length);
        Assert.Equal(string.Empty, Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public void
        WriteWithCommitTrackingRejectsTrailingInvalidUtf16ForStandardConsoleTextWriterBeforeOutput()
    {
        using var stream = new MemoryStream();
        ICommitTrackingDiagnosticSink sink = new TextWriterDiagnosticSink(
            new StandardConsoleTextWriter(
                stream,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true),
                Environment.NewLine));
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "safe message\uD83D",
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        DiagnosticWriteException exception = Assert.Throws<DiagnosticWriteException>(
            () => sink.WriteWithCommitTracking(diagnosticEvent));

        Assert.False(exception.OutputCommitted);
        Assert.IsType<EncoderFallbackException>(exception.OriginalException);
        Assert.Equal(0, stream.Length);
        Assert.Equal(string.Empty, Encoding.UTF8.GetString(stream.ToArray()));
    }

    [Fact]
    public void
        TextWriterUnicodeScalarWriterReportsNoCommittedOutputForOrdinaryStreamWriterEncodingFailure(
        )
    {
        using var stream = new MemoryStream();
        using var writer = new StreamWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        bool outputCommitted = false;

        Assert.Throws<EncoderFallbackException>(() => TextWriterUnicodeScalarWriter.Write(
            writer,
            "\uD83Dleading invalid utf16",
            ref outputCommitted,
            trackCommit: true));

        writer.Flush();

        Assert.False(outputCommitted);
        Assert.Equal(0, stream.Length);
    }

    [Fact]
    public void
        TextWriterUnicodeScalarWriterRejectsNonBmpForEncodingLyingStreamWriterBeforeOutput()
    {
        using var stream = new MemoryStream();
        using var writer = new EncodingLyingStreamWriter(
            stream,
            actualEncoding: Encoding.Latin1,
            reportedEncoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        bool outputCommitted = false;

        Assert.Throws<NotSupportedException>(() => TextWriterUnicodeScalarWriter.Write(
            writer,
            "safe 🚀",
            ref outputCommitted,
            trackCommit: true));

        writer.Flush();

        Assert.False(outputCommitted);
        Assert.Equal(0, stream.Length);
        Assert.Equal(string.Empty, Encoding.Latin1.GetString(stream.ToArray()));
    }

    [Fact]
    public void
        TextWriterUnicodeScalarWriterRejectsNonBmpForSynchronizedEncodingLyingStreamWriter()
    {
        using var stream = new MemoryStream();
        using var innerWriter = new EncodingLyingStreamWriter(
            stream,
            actualEncoding: Encoding.Latin1,
            reportedEncoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        TextWriter writer = TextWriter.Synchronized(innerWriter);
        bool outputCommitted = false;

        Assert.Throws<NotSupportedException>(() => TextWriterUnicodeScalarWriter.Write(
            writer,
            "safe 🚀",
            ref outputCommitted,
            trackCommit: true));

        innerWriter.Flush();

        Assert.False(outputCommitted);
        Assert.Equal(0, stream.Length);
        Assert.Equal(string.Empty, Encoding.Latin1.GetString(stream.ToArray()));
    }

    [Fact]
    public void WriteWithCommitTrackingRejectsCustomEncodingForExactStreamWriterBeforeOutput()
    {
        using var stream = new MemoryStream();
        using var writer = new StreamWriter(
            stream,
            new CloneBypassingReplacementEncoding(
                Encoding.Latin1,
                new UTF8Encoding(
                    encoderShouldEmitUTF8Identifier: false,
                    throwOnInvalidBytes: true)),
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        ICommitTrackingDiagnosticSink sink = new TextWriterDiagnosticSink(writer);
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "safe \u0100",
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        DiagnosticWriteException exception = Assert.Throws<DiagnosticWriteException>(
            () => sink.WriteWithCommitTracking(diagnosticEvent));

        writer.Flush();

        Assert.False(exception.OutputCommitted);
        Assert.IsType<NotSupportedException>(exception.OriginalException);
        Assert.Equal(0, stream.Length);
        Assert.Equal(string.Empty, Encoding.Latin1.GetString(stream.ToArray()));
    }

    [Fact]
    public void
        TextWriterUnicodeScalarWriterRejectsStreamWriterSubclassForUnencodableBmpBeforeOutput()
    {
        using var stream = new MemoryStream();
        using var writer = new DerivedLatin1StreamWriter(stream, bufferSize: 1024, leaveOpen: true)
        {
            AutoFlush = false,
        };
        bool outputCommitted = false;

        Assert.Throws<NotSupportedException>(() => TextWriterUnicodeScalarWriter.Write(
            writer,
            "safe \u0100",
            ref outputCommitted,
            trackCommit: true));

        writer.Flush();

        Assert.False(outputCommitted);
        Assert.Equal(0, stream.Length);
        Assert.Equal(string.Empty, Encoding.Latin1.GetString(stream.ToArray()));
    }

    [Fact]
    public void TextWriterUnicodeScalarWriterRejectsSyncSubclassForUnencodableBmpBeforeOutput()
    {
        using var stream = new MemoryStream();
        using var innerWriter = new DerivedLatin1StreamWriter(
            stream,
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        TextWriter writer = TextWriter.Synchronized(innerWriter);
        bool outputCommitted = false;

        Assert.Throws<NotSupportedException>(() => TextWriterUnicodeScalarWriter.Write(
            writer,
            "safe \u0100",
            ref outputCommitted,
            trackCommit: true));

        innerWriter.Flush();

        Assert.False(outputCommitted);
        Assert.Equal(0, stream.Length);
        Assert.Equal(string.Empty, Encoding.Latin1.GetString(stream.ToArray()));
    }

    [Fact]
    public void
        WriteWithCommitTrackingRejectsInvalidUtf16ForOrdinaryStreamWriterBeforeOutput(
        )
    {
        Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var stream = new MemoryStream();
        using var writer = new StreamWriter(
            stream,
            encoding,
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        ICommitTrackingDiagnosticSink sink = new TextWriterDiagnosticSink(writer);
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "\uD83Dleading invalid utf16",
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        DiagnosticWriteException exception = Assert.Throws<DiagnosticWriteException>(
            () => sink.WriteWithCommitTracking(diagnosticEvent));

        writer.Flush();

        Assert.False(exception.OutputCommitted);
        Assert.IsType<EncoderFallbackException>(exception.OriginalException);
        Assert.Equal(0, stream.Length);
        Assert.Equal(string.Empty, encoding.GetString(stream.ToArray()));
    }

    [Fact]
    public void
        WriteWithCommitTrackingRejectsUnencodableUnicodeForOrdinaryStreamWriterBeforeOutput(
        )
    {
        Encoding encoding = Encoding.Latin1;
        using var stream = new MemoryStream();
        using var writer = new StreamWriter(
            stream,
            encoding,
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        ICommitTrackingDiagnosticSink sink = new TextWriterDiagnosticSink(writer);
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "safe 🚀",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "ok 🧪",
            },
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        DiagnosticWriteException exception = Assert.Throws<DiagnosticWriteException>(
            () => sink.WriteWithCommitTracking(diagnosticEvent));

        writer.Flush();

        Assert.False(exception.OutputCommitted);
        Assert.IsType<EncoderFallbackException>(exception.OriginalException);
        Assert.Equal(0, stream.Length);
        Assert.Equal(string.Empty, encoding.GetString(stream.ToArray()));
    }

    [Fact]
    public void
        WriteWithCommitTrackingRejectsInvalidNewLineForOrdinaryStreamWriterWithoutLineLeakage()
    {
        Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var stream = new MemoryStream();
        using var writer = new StreamWriter(
            stream,
            encoding,
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
            NewLine = "\uD83D",
        };
        ICommitTrackingDiagnosticSink sink = new TextWriterDiagnosticSink(writer);
        var failedDiagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "first line",
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        DiagnosticWriteException exception = Assert.Throws<DiagnosticWriteException>(
            () => sink.WriteWithCommitTracking(failedDiagnosticEvent));

        writer.Flush();

        Assert.False(exception.OutputCommitted);
        Assert.IsType<EncoderFallbackException>(exception.OriginalException);
        Assert.Equal(0, stream.Length);
        Assert.Equal(string.Empty, encoding.GetString(stream.ToArray()));

        writer.NewLine = "\r\n";
        sink.Write(
            new DiagnosticEvent(
                DiagnosticSeverity.Information,
                DiagnosticChannel.Diagnostic,
                "second line",
                timestamp: DateTimeOffset.Parse("2025-01-02T03:04:06.0000000+00:00")));
        writer.Flush();

        Assert.Equal(
            "2025-01-02T03:04:06.0000000+00:00 [Information] second line\r\n",
            encoding.GetString(stream.ToArray()));
    }

    [Fact]
    public void
        WriteWithCommitTrackingRejectsInvalidUtf16ForSynchronizedStreamWriterBeforeOutput()
    {
        Encoding encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var stream = new MemoryStream();
        using var innerWriter = new StreamWriter(
            stream,
            encoding,
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        ICommitTrackingDiagnosticSink sink = new TextWriterDiagnosticSink(
            TextWriter.Synchronized(innerWriter));
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "\uD83Dleading invalid utf16",
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        DiagnosticWriteException exception = Assert.Throws<DiagnosticWriteException>(
            () => sink.WriteWithCommitTracking(diagnosticEvent));

        innerWriter.Flush();

        Assert.False(exception.OutputCommitted);
        Assert.IsType<EncoderFallbackException>(exception.OriginalException);
        Assert.Equal(0, stream.Length);
        Assert.Equal(string.Empty, encoding.GetString(stream.ToArray()));
    }

    [Fact]
    public void
        WriteWithCommitTrackingRejectsUnencodableNewLineForSyncStreamWriterWithoutLeakage()
    {
        Encoding encoding = Encoding.Latin1;
        using var stream = new MemoryStream();
        using var innerWriter = new StreamWriter(
            stream,
            encoding,
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
            NewLine = "🚀",
        };
        TextWriter writer = TextWriter.Synchronized(innerWriter);
        ICommitTrackingDiagnosticSink sink = new TextWriterDiagnosticSink(writer);
        var failedDiagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "first line",
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        DiagnosticWriteException exception = Assert.Throws<DiagnosticWriteException>(
            () => sink.WriteWithCommitTracking(failedDiagnosticEvent));

        innerWriter.Flush();

        Assert.False(exception.OutputCommitted);
        Assert.IsType<EncoderFallbackException>(exception.OriginalException);
        Assert.Equal(0, stream.Length);
        Assert.Equal(string.Empty, encoding.GetString(stream.ToArray()));

        writer.NewLine = "\r\n";
        sink.Write(
            new DiagnosticEvent(
                DiagnosticSeverity.Information,
                DiagnosticChannel.Diagnostic,
                "second line",
                timestamp: DateTimeOffset.Parse("2025-01-02T03:04:06.0000000+00:00")));
        innerWriter.Flush();

        Assert.Equal(
            "2025-01-02T03:04:06.0000000+00:00 [Information] second line\r\n",
            encoding.GetString(stream.ToArray()));
    }

    [Fact]
    public void
        WriteWithCommitTrackingRejectsUnencodableUnicodeForSynchronizedStreamWriterBeforeOutput(
        )
    {
        Encoding encoding = Encoding.Latin1;
        using var stream = new MemoryStream();
        using var innerWriter = new StreamWriter(
            stream,
            encoding,
            bufferSize: 1024,
            leaveOpen: true)
        {
            AutoFlush = false,
        };
        ICommitTrackingDiagnosticSink sink = new TextWriterDiagnosticSink(
            TextWriter.Synchronized(innerWriter));
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "safe 🚀",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "ok 🧪",
            },
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        DiagnosticWriteException exception = Assert.Throws<DiagnosticWriteException>(
            () => sink.WriteWithCommitTracking(diagnosticEvent));

        innerWriter.Flush();

        Assert.False(exception.OutputCommitted);
        Assert.IsType<EncoderFallbackException>(exception.OriginalException);
        Assert.Equal(0, stream.Length);
        Assert.Equal(string.Empty, encoding.GetString(stream.ToArray()));
    }

    [Fact]
    public void StandardConsoleTextWriterSeparateWrappersOverSameStreamSerializeWriteWithProgress()
    {
        using var stream = new CoordinatedWriteStream(Encoding.UTF8);
        var firstWriter = new StandardConsoleTextWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            Environment.NewLine);
        var secondWriter = new StandardConsoleTextWriter(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            Environment.NewLine);
        const string firstPayload = "alpha 🚀";
        const string secondPayload = "beta 🛰️";
        using var writersReady = new CountdownEvent(2);
        using var releaseWriters = new ManualResetEventSlim(false);
        var firstCharsWritten = 0;
        var secondCharsWritten = 0;
        Exception? firstException = null;
        Exception? secondException = null;

        var firstThread = new Thread(() =>
        {
            writersReady.Signal();
            releaseWriters.Wait();
            try
            {
                firstWriter.WriteWithProgress(firstPayload.AsSpan(), ref firstCharsWritten);
            }
            catch (Exception ex)
            {
                firstException = ex;
            }
        });
        var secondThread = new Thread(() =>
        {
            writersReady.Signal();
            releaseWriters.Wait();
            try
            {
                secondWriter.WriteWithProgress(secondPayload.AsSpan(), ref secondCharsWritten);
            }
            catch (Exception ex)
            {
                secondException = ex;
            }
        });

        firstThread.Start();
        secondThread.Start();
        Assert.True(
            writersReady.Wait(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken));
        releaseWriters.Set();

        Assert.True(stream.WaitForPendingWrite(TimeSpan.FromSeconds(10)));
        Assert.False(
            stream.WaitForDistinctPendingThreadCount(
                minimumDistinctPendingThreadCount: 2,
                TimeSpan.FromMilliseconds(500)),
            "Separate StandardConsoleTextWriter wrappers should share one stream-scoped lock.");
        Assert.True(stream.TryReleaseNextPendingWrite(preferredDifferentThreadId: null, out _));
        Assert.True(stream.WaitForPendingWrite(TimeSpan.FromSeconds(10)));
        Assert.True(stream.TryReleaseNextPendingWrite(preferredDifferentThreadId: null, out _));

        Assert.True(firstThread.Join(TimeSpan.FromSeconds(10)));
        Assert.True(secondThread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(firstException);
        Assert.Null(secondException);
        Assert.Equal(firstPayload.Length, firstCharsWritten);
        Assert.Equal(secondPayload.Length, secondCharsWritten);

        string written = stream.Written;
        Assert.True(
            string.Equals(written, firstPayload + secondPayload, StringComparison.Ordinal)
                || string.Equals(written, secondPayload + firstPayload, StringComparison.Ordinal),
            $"Unexpected standard console output ordering: '{written}'.");
        AssertWritesContainNoIsolatedSurrogates(stream.Writes);
    }

    [Fact]
    public void
        WriteWithCommitTrackingSerializesSeparateBufferedStandardConsoleTextWritersOverSameStream()
    {
        using var stream = new CoordinatedWriteStream(Encoding.UTF8);
        using var firstBufferedStream = new BufferedStream(stream, bufferSize: 1024);
        using var secondBufferedStream = new BufferedStream(stream, bufferSize: 1024);
        Encoding encoding = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true);
        ICommitTrackingDiagnosticSink firstSink = new TextWriterDiagnosticSink(
            new StandardConsoleTextWriter(
                firstBufferedStream,
                encoding,
                Environment.NewLine));
        ICommitTrackingDiagnosticSink secondSink = new TextWriterDiagnosticSink(
            new StandardConsoleTextWriter(
                secondBufferedStream,
                encoding,
                Environment.NewLine));
        DateTimeOffset firstTimestamp = DateTimeOffset.Parse("1111-01-02T03:04:05.0000000+00:00");
        DateTimeOffset secondTimestamp = DateTimeOffset.Parse("9999-12-30T23:58:57.0000000+00:00");
        var firstEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "alpha line",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "one",
            },
            timestamp: firstTimestamp);
        var secondEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "beta line",
            properties: new Dictionary<string, string?>
            {
                ["detail"] = "two",
            },
            timestamp: secondTimestamp);
        using var writersReady = new CountdownEvent(2);
        using var releaseWriters = new ManualResetEventSlim(false);
        bool? firstOutputCommitted = null;
        bool? secondOutputCommitted = null;
        Exception? firstException = null;
        Exception? secondException = null;

        var firstThread = new Thread(() =>
        {
            writersReady.Signal();
            releaseWriters.Wait();
            try
            {
                firstOutputCommitted = firstSink.WriteWithCommitTracking(firstEvent);
            }
            catch (Exception ex)
            {
                firstException = ex;
            }
        });
        var secondThread = new Thread(() =>
        {
            writersReady.Signal();
            releaseWriters.Wait();
            try
            {
                secondOutputCommitted = secondSink.WriteWithCommitTracking(secondEvent);
            }
            catch (Exception ex)
            {
                secondException = ex;
            }
        });

        firstThread.Start();
        secondThread.Start();
        Assert.True(
            writersReady.Wait(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken));
        releaseWriters.Set();

        Assert.True(stream.WaitForPendingWrite(TimeSpan.FromSeconds(10)));
        bool observedTwoPendingThreads = stream.WaitForDistinctPendingThreadCount(
            minimumDistinctPendingThreadCount: 2,
            TimeSpan.FromMilliseconds(500));

        DateTime releaseDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(20);
        int? lastReleasedThreadId = null;
        while (firstThread.IsAlive || secondThread.IsAlive)
        {
            if (!stream.WaitForPendingWrite(TimeSpan.FromMilliseconds(200)))
            {
                Assert.True(
                    DateTime.UtcNow < releaseDeadline,
                    "Timed out waiting for the next buffered standard console diagnostic "
                        + "chunk.");
                continue;
            }

            if (!stream.TryReleaseNextPendingWrite(lastReleasedThreadId, out int releasedThreadId))
            {
                continue;
            }

            lastReleasedThreadId = releasedThreadId;
        }

        Assert.True(firstThread.Join(TimeSpan.FromSeconds(10)));
        Assert.True(secondThread.Join(TimeSpan.FromSeconds(10)));
        Assert.Null(firstException);
        Assert.Null(secondException);
        Assert.True(firstOutputCommitted);
        Assert.True(secondOutputCommitted);
        Assert.False(
            observedTwoPendingThreads,
            "Separate StandardConsoleTextWriter sinks over BufferedStream wrappers should "
                + "share one downstream stream-scoped lock.");

        string firstLine =
            "1111-01-02T03:04:05.0000000+00:00 [Information] alpha line detail=one"
            + Environment.NewLine;
        string secondLine =
            "9999-12-30T23:58:57.0000000+00:00 [Information] beta line detail=two"
            + Environment.NewLine;
        string written = stream.Written;
        Assert.True(
            string.Equals(written, firstLine + secondLine, StringComparison.Ordinal)
                || string.Equals(written, secondLine + firstLine, StringComparison.Ordinal),
            $"Unexpected diagnostic output: '{written}'.");
    }

    [Fact]
    public void
        WriteWithCommitTrackingReportsCommittedOutputForPartialStandardConsoleStreamFailure()
    {
        using var stream = new PartiallyWritingThrowingStream(
            bytesToWriteBeforeThrow: 1,
            new IOException("diagnostic write failed"));
        ICommitTrackingDiagnosticSink sink = new TextWriterDiagnosticSink(
            new StandardConsoleTextWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                Environment.NewLine));
        var diagnosticEvent = new DiagnosticEvent(
            DiagnosticSeverity.Information,
            DiagnosticChannel.Diagnostic,
            "safe message",
            timestamp: DateTimeOffset.Parse("2025-01-02T03:04:05.0000000+00:00"));

        DiagnosticWriteException exception = Assert.Throws<DiagnosticWriteException>(
            () => sink.WriteWithCommitTracking(diagnosticEvent));

        Assert.True(exception.OutputCommitted);
        Assert.IsType<IOException>(exception.OriginalException);
        Assert.Equal("diagnostic write failed", exception.OriginalException.Message);
        Assert.Equal(1, stream.WrittenByteCount);
    }

    private static void AssertWritesContainNoIsolatedSurrogates(IEnumerable<string> writes)
    {
        foreach (string write in writes)
        {
            for (var index = 0; index < write.Length; index++)
            {
                if (char.IsHighSurrogate(write[index]))
                {
                    Assert.True(
                        index + 1 < write.Length && char.IsLowSurrogate(write[index + 1]),
                        $"Write chunk contained an isolated high surrogate: '{write}'.");
                    index++;
                    continue;
                }

                Assert.False(
                    char.IsLowSurrogate(write[index]),
                    $"Write chunk contained an isolated low surrogate: '{write}'.");
            }
        }
    }

    private static Encoding? TryCreateUtf7Encoding()
    {
        Type? utf7EncodingType = typeof(Encoding).Assembly.GetType(
            "System.Text.UTF7Encoding",
            throwOnError: false);
        if (utf7EncodingType is null)
        {
            return null;
        }

        try
        {
            return Activator.CreateInstance(utf7EncodingType) as Encoding;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (System.Reflection.TargetInvocationException ex)
            when (ex.InnerException is NotSupportedException)
        {
            return null;
        }
    }

    private sealed class StringOnlyTextWriter : TextWriter, IProgressAwareTextWriter
    {
        private readonly List<string> _writes = [];
        private readonly StringBuilder _written = new();

        public override Encoding Encoding => Encoding.UTF8;

        public string Written => _written.ToString();

        public IReadOnlyList<string> Writes => _writes;

        public override void Write(string? value)
        {
            if (value is not null)
            {
                _writes.Add(value);
                _written.Append(value);
            }
        }

        public void WriteWithProgress(ReadOnlySpan<char> value, ref int charsWritten)
        {
            string chunk = new(value);
            Write(chunk);
            charsWritten += chunk.Length;
        }
    }

    private sealed class AppendThenThrowStringTextWriter : TextWriter
    {
        private readonly Exception _exceptionToThrow;
        private readonly StringBuilder _written = new();

        public AppendThenThrowStringTextWriter(Exception exceptionToThrow)
        {
            _exceptionToThrow = exceptionToThrow;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public string Written => _written.ToString();

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            _written.Append(value);
            throw _exceptionToThrow;
        }
    }

    private sealed class ThrowingProgressAwareTextWriter : TextWriter, IProgressAwareTextWriter
    {
        private readonly int _charsWrittenBeforeThrow;
        private readonly Exception _exceptionToThrow;
        private readonly StringBuilder _written = new();

        public ThrowingProgressAwareTextWriter(
            int charsWrittenBeforeThrow,
            Exception exceptionToThrow)
        {
            _charsWrittenBeforeThrow = charsWrittenBeforeThrow;
            _exceptionToThrow = exceptionToThrow;
        }

        public override Encoding Encoding => Encoding.UTF8;

        public string Written => _written.ToString();

        public void WriteWithProgress(ReadOnlySpan<char> value, ref int charsWritten)
        {
            int charactersToWrite = Math.Min(_charsWrittenBeforeThrow, value.Length);
            if (charactersToWrite > 0)
            {
                _written.Append(value[..charactersToWrite]);
                charsWritten += charactersToWrite;
            }

            throw _exceptionToThrow;
        }
    }

    private sealed class CoordinatedProgressAwareTextWriter : TextWriter, IProgressAwareTextWriter
    {
        private readonly List<PendingChunk> _pendingChunks = [];
        private readonly object _syncRoot = new();
        private readonly List<string> _writes = [];
        private readonly StringBuilder _written = new();

        public override Encoding Encoding => Encoding.UTF8;

        public string Written
        {
            get
            {
                lock (_syncRoot)
                {
                    return _written.ToString();
                }
            }
        }

        public IReadOnlyList<string> Writes
        {
            get
            {
                lock (_syncRoot)
                {
                    return _writes.ToArray();
                }
            }
        }

        public bool WaitForPendingWrite(TimeSpan timeout)
        {
            return WaitUntil(HasUnreleasedPendingChunk, timeout);
        }

        public bool WaitForDistinctPendingThreadCount(
            int minimumDistinctPendingThreadCount,
            TimeSpan timeout)
        {
            return WaitUntil(
                () => GetDistinctPendingThreadCount() >= minimumDistinctPendingThreadCount,
                timeout);
        }

        public bool TryReleaseNextPendingChunk(
            int? preferredDifferentThreadId,
            out int releasedThreadId)
        {
            lock (_syncRoot)
            {
                PendingChunk? pendingChunk =
                    FindPendingChunk(preferredDifferentThreadId, requireDifferentThreadId: true)
                    ?? FindPendingChunk(
                        preferredDifferentThreadId,
                        requireDifferentThreadId: false);
                if (pendingChunk is null)
                {
                    releasedThreadId = 0;
                    return false;
                }

                pendingChunk.IsReleased = true;
                releasedThreadId = pendingChunk.ThreadId;
                pendingChunk.Release.Set();
                return true;
            }
        }

        public void WriteWithProgress(ReadOnlySpan<char> value, ref int charsWritten)
        {
            string chunk = new(value);
            var pendingChunk = new PendingChunk(Environment.CurrentManagedThreadId);
            lock (_syncRoot)
            {
                _pendingChunks.Add(pendingChunk);
                Monitor.PulseAll(_syncRoot);
            }

            try
            {
                if (!pendingChunk.Release.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException(
                        "Timed out waiting to release a coordinated diagnostic chunk.");
                }

                lock (_syncRoot)
                {
                    _writes.Add(chunk);
                    _written.Append(chunk);
                }

                charsWritten += chunk.Length;
            }
            finally
            {
                lock (_syncRoot)
                {
                    _pendingChunks.Remove(pendingChunk);
                    Monitor.PulseAll(_syncRoot);
                }

                pendingChunk.Release.Dispose();
            }
        }

        private PendingChunk? FindPendingChunk(
            int? preferredDifferentThreadId,
            bool requireDifferentThreadId)
        {
            foreach (PendingChunk pendingChunk in _pendingChunks)
            {
                if (pendingChunk.IsReleased)
                {
                    continue;
                }

                if (requireDifferentThreadId
                    && preferredDifferentThreadId.HasValue
                    && pendingChunk.ThreadId == preferredDifferentThreadId.Value)
                {
                    continue;
                }

                return pendingChunk;
            }

            return null;
        }

        private int GetDistinctPendingThreadCount()
        {
            int? firstThreadId = null;
            var distinctThreadCount = 0;
            foreach (PendingChunk pendingChunk in _pendingChunks)
            {
                if (pendingChunk.IsReleased)
                {
                    continue;
                }

                if (firstThreadId is null)
                {
                    firstThreadId = pendingChunk.ThreadId;
                    distinctThreadCount = 1;
                    continue;
                }

                if (pendingChunk.ThreadId != firstThreadId.Value)
                {
                    return 2;
                }
            }

            return distinctThreadCount;
        }

        private bool HasUnreleasedPendingChunk()
        {
            foreach (PendingChunk pendingChunk in _pendingChunks)
            {
                if (!pendingChunk.IsReleased)
                {
                    return true;
                }
            }

            return false;
        }

        private bool WaitUntil(Func<bool> predicate, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            lock (_syncRoot)
            {
                while (!predicate())
                {
                    TimeSpan remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        return false;
                    }

                    Monitor.Wait(_syncRoot, remaining);
                }

                return true;
            }
        }

        private sealed class PendingChunk
        {
            public PendingChunk(int threadId)
            {
                ThreadId = threadId;
            }

            public bool IsReleased { get; set; }

            public ManualResetEventSlim Release { get; } = new(false);

            public int ThreadId { get; }
        }
    }

    private sealed class CoordinatedWriteStream : Stream
    {
        private readonly Encoding _encoding;
        private readonly List<PendingWrite> _pendingWrites = [];
        private readonly object _syncRoot = new();
        private readonly List<string> _writes = [];
        private readonly StringBuilder _written = new();

        public CoordinatedWriteStream(Encoding encoding)
        {
            _encoding = encoding;
        }

        public IReadOnlyList<string> Writes
        {
            get
            {
                lock (_syncRoot)
                {
                    return _writes.ToArray();
                }
            }
        }

        public string Written
        {
            get
            {
                lock (_syncRoot)
                {
                    return _written.ToString();
                }
            }
        }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public bool WaitForPendingWrite(TimeSpan timeout)
        {
            return WaitUntil(HasUnreleasedPendingWrite, timeout);
        }

        public bool WaitForDistinctPendingThreadCount(
            int minimumDistinctPendingThreadCount,
            TimeSpan timeout)
        {
            return WaitUntil(
                () => GetDistinctPendingThreadCount() >= minimumDistinctPendingThreadCount,
                timeout);
        }

        public bool TryReleaseNextPendingWrite(
            int? preferredDifferentThreadId,
            out int releasedThreadId)
        {
            lock (_syncRoot)
            {
                PendingWrite? pendingWrite =
                    FindPendingWrite(preferredDifferentThreadId, requireDifferentThreadId: true)
                    ?? FindPendingWrite(
                        preferredDifferentThreadId,
                        requireDifferentThreadId: false);
                if (pendingWrite is null)
                {
                    releasedThreadId = 0;
                    return false;
                }

                pendingWrite.IsReleased = true;
                releasedThreadId = pendingWrite.ThreadId;
                pendingWrite.Release.Set();
                return true;
            }
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            string chunk = _encoding.GetString(buffer, offset, count);
            var pendingWrite = new PendingWrite(Environment.CurrentManagedThreadId);
            lock (_syncRoot)
            {
                _pendingWrites.Add(pendingWrite);
                Monitor.PulseAll(_syncRoot);
            }

            try
            {
                if (!pendingWrite.Release.Wait(TimeSpan.FromSeconds(10)))
                {
                    throw new TimeoutException(
                        "Timed out waiting to release a coordinated standard console write.");
                }

                lock (_syncRoot)
                {
                    _writes.Add(chunk);
                    _written.Append(chunk);
                }
            }
            finally
            {
                lock (_syncRoot)
                {
                    _pendingWrites.Remove(pendingWrite);
                    Monitor.PulseAll(_syncRoot);
                }

                pendingWrite.Release.Dispose();
            }
        }

        private PendingWrite? FindPendingWrite(
            int? preferredDifferentThreadId,
            bool requireDifferentThreadId)
        {
            foreach (PendingWrite pendingWrite in _pendingWrites)
            {
                if (pendingWrite.IsReleased)
                {
                    continue;
                }

                if (requireDifferentThreadId
                    && preferredDifferentThreadId.HasValue
                    && pendingWrite.ThreadId == preferredDifferentThreadId.Value)
                {
                    continue;
                }

                return pendingWrite;
            }

            return null;
        }

        private int GetDistinctPendingThreadCount()
        {
            int? firstThreadId = null;
            var distinctThreadCount = 0;
            foreach (PendingWrite pendingWrite in _pendingWrites)
            {
                if (pendingWrite.IsReleased)
                {
                    continue;
                }

                if (firstThreadId is null)
                {
                    firstThreadId = pendingWrite.ThreadId;
                    distinctThreadCount = 1;
                    continue;
                }

                if (pendingWrite.ThreadId != firstThreadId.Value)
                {
                    return 2;
                }
            }

            return distinctThreadCount;
        }

        private bool HasUnreleasedPendingWrite()
        {
            foreach (PendingWrite pendingWrite in _pendingWrites)
            {
                if (!pendingWrite.IsReleased)
                {
                    return true;
                }
            }

            return false;
        }

        private bool WaitUntil(Func<bool> predicate, TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            lock (_syncRoot)
            {
                while (!predicate())
                {
                    TimeSpan remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                    {
                        return false;
                    }

                    Monitor.Wait(_syncRoot, remaining);
                }

                return true;
            }
        }

        private sealed class PendingWrite
        {
            public PendingWrite(int threadId)
            {
                ThreadId = threadId;
            }

            public bool IsReleased { get; set; }

            public ManualResetEventSlim Release { get; } = new(false);

            public int ThreadId { get; }
        }
    }

    private sealed class ExternallyWritingThrowingStringWriter : StringWriter
    {
        private readonly Exception _exceptionToThrow;
        private readonly StringBuilder _written = new();

        public ExternallyWritingThrowingStringWriter(Exception exceptionToThrow)
        {
            _exceptionToThrow = exceptionToThrow;
        }

        public string Written => _written.ToString();

        public override void Write(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            _written.Append(value);
            throw _exceptionToThrow;
        }
    }

    private sealed class PartiallyWritingThrowingStream : Stream
    {
        private readonly int _bytesToWriteBeforeThrow;
        private readonly Exception _exceptionToThrow;
        private readonly MemoryStream _written = new();

        public PartiallyWritingThrowingStream(
            int bytesToWriteBeforeThrow,
            Exception exceptionToThrow)
        {
            _bytesToWriteBeforeThrow = bytesToWriteBeforeThrow;
            _exceptionToThrow = exceptionToThrow;
        }

        public int WrittenByteCount => checked((int)_written.Length);

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => _written.Length;

        public override long Position
        {
            get => _written.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            int bytesToWrite = Math.Min(count, _bytesToWriteBeforeThrow);
            if (bytesToWrite != 0)
            {
                _written.Write(buffer, offset, bytesToWrite);
            }

            throw _exceptionToThrow;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _written.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class EncodingLyingStreamWriter : StreamWriter
    {
        private readonly Encoding _reportedEncoding;

        public EncodingLyingStreamWriter(
            Stream stream,
            Encoding actualEncoding,
            Encoding reportedEncoding,
            int bufferSize,
            bool leaveOpen)
            : base(stream, actualEncoding, bufferSize, leaveOpen)
        {
            _reportedEncoding = reportedEncoding;
        }

        public override Encoding Encoding => _reportedEncoding;
    }

    private sealed class DerivedLatin1StreamWriter : StreamWriter
    {
        public DerivedLatin1StreamWriter(Stream stream, int bufferSize, bool leaveOpen)
            : base(stream, Encoding.Latin1, bufferSize, leaveOpen)
        {
        }
    }

    private sealed class CloneBypassingReplacementEncoding : Encoding
    {
        private readonly Encoding _strictCloneEncoding;
        private readonly Encoding _writeEncoding;

        public CloneBypassingReplacementEncoding(
            Encoding writeEncoding,
            Encoding strictCloneEncoding)
        {
            _writeEncoding = writeEncoding
                ?? throw new ArgumentNullException(nameof(writeEncoding));
            _strictCloneEncoding = strictCloneEncoding
                ?? throw new ArgumentNullException(nameof(strictCloneEncoding));
        }

        public override object Clone()
        {
            return _strictCloneEncoding.Clone();
        }

        public override int GetByteCount(char[] chars, int index, int count)
        {
            return _writeEncoding.GetByteCount(chars, index, count);
        }

        public override int GetBytes(
            char[] chars,
            int charIndex,
            int charCount,
            byte[] bytes,
            int byteIndex)
        {
            return _writeEncoding.GetBytes(chars, charIndex, charCount, bytes, byteIndex);
        }

        public override int GetCharCount(byte[] bytes, int index, int count)
        {
            return _writeEncoding.GetCharCount(bytes, index, count);
        }

        public override int GetChars(
            byte[] bytes,
            int byteIndex,
            int byteCount,
            char[] chars,
            int charIndex)
        {
            return _writeEncoding.GetChars(bytes, byteIndex, byteCount, chars, charIndex);
        }

        public override int GetMaxByteCount(int charCount)
        {
            return _writeEncoding.GetMaxByteCount(charCount);
        }

        public override int GetMaxCharCount(int byteCount)
        {
            return _writeEncoding.GetMaxCharCount(byteCount);
        }

        public override byte[] GetPreamble()
        {
            return _writeEncoding.GetPreamble();
        }

        public override Decoder GetDecoder()
        {
            return _writeEncoding.GetDecoder();
        }

        public override Encoder GetEncoder()
        {
            return _writeEncoding.GetEncoder();
        }
    }

    private sealed class CharOnlyTextWriter : TextWriter
    {
        private readonly StringBuilder _written = new();

        public override Encoding Encoding => Encoding.UTF8;

        public string Written => _written.ToString();

        public override void Write(char value)
        {
            _written.Append(value);
        }
    }

    private sealed class SpanOnlyTextWriter : TextWriter
    {
        private readonly StringBuilder _written = new();

        public override Encoding Encoding => Encoding.UTF8;

        public string Written => _written.ToString();

        public override void Write(ReadOnlySpan<char> buffer)
        {
            _written.Append(buffer);
        }
    }

    private sealed class ForwardingStringTextWriter : TextWriter
    {
        private readonly StringBuilder _written = new();

        public override Encoding Encoding => Encoding.UTF8;

        public string Written => _written.ToString();

        public override void Write(char value)
        {
            _written.Append(value);
        }

        public override void Write(string? value)
        {
            base.Write(value);
        }
    }
}
