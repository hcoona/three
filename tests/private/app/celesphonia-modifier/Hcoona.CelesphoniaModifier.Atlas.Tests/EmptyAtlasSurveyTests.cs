using Hcoona.CelesphoniaModifier.Atlas;
using System.Text.Json;
using Xunit;

namespace Hcoona.CelesphoniaModifier.Atlas.Tests;

public sealed class EmptyAtlasSurveyTests
{
    private static readonly byte[] ExpectedBytes =
        "{\"schemaVersion\":\"atlas-empty-survey/v1\",\"observations\":[]}\n"u8.ToArray();

    [Fact]
    public void SchemaVersionMatchesDocumentContract()
    {
        Assert.Equal("atlas-empty-survey/v1", EmptyAtlasSurvey.SchemaVersion);
    }

    [Fact]
    public async Task WriteAsyncWritesExactDocumentOnce()
    {
        RecordingStream destination = new();

        await EmptyAtlasSurvey.WriteAsync(destination, TestContext.Current.CancellationToken);

        Assert.Equal(60, destination.Bytes.Length);
        Assert.Equal(ExpectedBytes, destination.Bytes);
        Assert.Equal(1, destination.WriteCallCount);
        Assert.Equal(0, destination.FlushCallCount);
        Assert.Equal(0, destination.DisposeCallCount);
    }

    [Fact]
    public async Task RepeatedWritesAreByteIdentical()
    {
        using MemoryStream first = new();
        using MemoryStream second = new();

        await EmptyAtlasSurvey.WriteAsync(first, TestContext.Current.CancellationToken);
        await EmptyAtlasSurvey.WriteAsync(second, TestContext.Current.CancellationToken);

        Assert.Equal(first.ToArray(), second.ToArray());
    }

    [Fact]
    public async Task OutputHasExactJsonShape()
    {
        using MemoryStream destination = new();
        await EmptyAtlasSurvey.WriteAsync(destination, TestContext.Current.CancellationToken);

        using JsonDocument document = JsonDocument.Parse(destination.ToArray());
        JsonProperty[] properties = document.RootElement.EnumerateObject().ToArray();

        Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
        Assert.Equal(2, properties.Length);
        Assert.Equal("schemaVersion", properties[0].Name);
        Assert.Equal(EmptyAtlasSurvey.SchemaVersion, properties[0].Value.GetString());
        Assert.Equal("observations", properties[1].Name);
        Assert.Equal(JsonValueKind.Array, properties[1].Value.ValueKind);
        Assert.Empty(properties[1].Value.EnumerateArray());
    }

    [Fact]
    public async Task NullDestinationPrecedesCancellation()
    {
        using CancellationTokenSource source = new();
        await source.CancelAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => EmptyAtlasSurvey.WriteAsync(null!, source.Token).AsTask());
    }

    [Fact]
    public async Task PreCancellationPrecedesWritableCheck()
    {
        using CancellationTokenSource source = new();
        await source.CancelAsync();
        RecordingStream destination = new(canWrite: false);

        OperationCanceledException exception =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => EmptyAtlasSurvey.WriteAsync(destination, source.Token).AsTask());

        Assert.Equal(source.Token, exception.CancellationToken);
        Assert.Equal(0, destination.WriteCallCount);
    }

    [Fact]
    public async Task PreCancellationPreventsWritableStreamWrite()
    {
        using CancellationTokenSource source = new();
        await source.CancelAsync();
        RecordingStream destination = new();

        OperationCanceledException exception =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => EmptyAtlasSurvey.WriteAsync(destination, source.Token).AsTask());

        Assert.Equal(source.Token, exception.CancellationToken);
        Assert.Equal(0, destination.WriteCallCount);
    }

    [Fact]
    public async Task NonWritableDestinationThrowsNotSupported()
    {
        RecordingStream destination = new(canWrite: false);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => EmptyAtlasSurvey.WriteAsync(
                destination,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task DisposedDestinationThrowsNotSupported()
    {
        MemoryStream destination = new();
        await destination.DisposeAsync();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => EmptyAtlasSurvey.WriteAsync(
                destination,
                TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task WritableGetterFailurePropagates()
    {
        InvalidOperationException expected = new("synthetic getter failure");
        RecordingStream destination = new(canWriteException: expected);

        InvalidOperationException actual =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => EmptyAtlasSurvey.WriteAsync(
                    destination,
                    TestContext.Current.CancellationToken).AsTask());

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task WriteCancellationReceivesCallerToken()
    {
        using CancellationTokenSource source = new();
        RecordingStream destination = new(
            write: (_, token) =>
                ValueTask.FromException(new OperationCanceledException(token)));

        OperationCanceledException exception =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => EmptyAtlasSurvey.WriteAsync(destination, source.Token).AsTask());

        Assert.Equal(source.Token, destination.LastWriteToken);
        Assert.Equal(source.Token, exception.CancellationToken);
    }

    [Fact]
    public async Task WriteFailurePropagates()
    {
        IOException expected = new("synthetic write failure");
        RecordingStream destination = new(
            write: (_, _) => ValueTask.FromException(expected));

        IOException actual = await Assert.ThrowsAsync<IOException>(
            () => EmptyAtlasSurvey.WriteAsync(
                destination,
                TestContext.Current.CancellationToken).AsTask());

        Assert.Same(expected, actual);
    }

    private sealed class RecordingStream(
        bool canWrite = true,
        Exception? canWriteException = null,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? write = null)
        : Stream
    {
        private readonly MemoryStream buffer = new();

        public byte[] Bytes => buffer.ToArray();

        public int DisposeCallCount { get; private set; }

        public int FlushCallCount { get; private set; }

        public CancellationToken LastWriteToken { get; private set; }

        public int WriteCallCount { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite =>
            canWriteException is null ? canWrite : throw canWriteException;

        public override long Length => buffer.Length;

        public override long Position
        {
            get => buffer.Position;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
            FlushCallCount++;
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            FlushCallCount++;
            return Task.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> source,
            CancellationToken cancellationToken = default)
        {
            WriteCallCount++;
            LastWriteToken = cancellationToken;
            if (write is not null)
            {
                return write(source, cancellationToken);
            }

            buffer.Write(source.Span);
            return ValueTask.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            DisposeCallCount++;
            if (disposing)
            {
                buffer.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
