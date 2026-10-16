using System.Security.Cryptography;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using WorkflowDelivery.Destinations;

namespace WorkflowDelivery.Tests.Release;

[TestClass]
public sealed class RegistryBodyTests(TestContext context)
{
    private static readonly byte[] Original = [1, 2, 3, 4];

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OriginalHashesTheBodyAndRetainsOnlyWhenRequested(bool retain)
    {
        using var stream = new MemoryStream(Original);
        RegistryOriginalBytes served = await RegistryBody.OriginalAsync(stream, 4, 4, retain,
            context.CancellationToken);
        Assert.AreEqual(4, served.Size);
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(Original)), served.Sha256);
        if (retain) CollectionAssert.AreEqual(Original, served.Bytes);
        else Assert.IsNull(served.Bytes);
    }

    // A body spanning many short reads is hashed and retained across every chunk.
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task OriginalAccumulatesAcrossShortReads(bool retain)
    {
        byte[] original = new byte[200 * 1024 + 3];
        new Random(29).NextBytes(original);
        using var stream = new ShortReadStream(original, 7);
        RegistryOriginalBytes served = await RegistryBody.OriginalAsync(stream,
            original.LongLength, original.LongLength, retain, context.CancellationToken);
        Assert.AreEqual(original.LongLength, served.Size);
        Assert.AreEqual(Convert.ToHexStringLower(SHA256.HashData(original)), served.Sha256);
        if (retain) CollectionAssert.AreEqual(original, served.Bytes);
        else Assert.IsNull(served.Bytes);
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow(5L)]
    public async Task LongerBodyStopsOnePastTheOriginalWithoutAMeasurement(long? declared)
    {
        using var stream = new CountingStream(new byte[64]);
        RegistryOriginalBytes served = await RegistryBody.OriginalAsync(stream, declared, 4,
            retain: true, context.CancellationToken);
        Assert.IsNull(served.Size);
        Assert.IsNull(served.Sha256);
        Assert.IsNull(served.Bytes);
        Assert.IsLessThanOrEqualTo(5, stream.Consumed);
    }

    [TestMethod]
    public async Task LongerDeclaredLengthWithTruncatedBodyIsIncomplete()
    {
        // A declared length is not the served bytes; a short transfer stays incomplete.
        using var truncated = new MemoryStream(Original[..3]);
        await Assert.ThrowsExactlyAsync<IOException>(() => RegistryBody.OriginalAsync(
            truncated, 5, 4, retain: true, context.CancellationToken));
    }

    [TestMethod]
    public async Task ShorterOrTruncatedBodiesStayDistinguishable()
    {
        using var shorter = new MemoryStream(Original[..3]);
        RegistryOriginalBytes served = await RegistryBody.OriginalAsync(shorter, null, 4,
            retain: false, context.CancellationToken);
        Assert.AreEqual(3, served.Size);
        using var truncated = new MemoryStream(Original[..3]);
        await Assert.ThrowsExactlyAsync<IOException>(() => RegistryBody.OriginalAsync(
            truncated, 4, 4, retain: false, context.CancellationToken));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow(5L)]
    public async Task MetadataKeepsItsInMemoryBound(long? declared)
    {
        using var stream = new MemoryStream(new byte[5]);
        await Assert.ThrowsExactlyAsync<IOException>(() => RegistryBody.LimitedAsync(stream,
            declared, 4, context.CancellationToken));
    }

    private sealed class ShortReadStream(byte[] bytes, int chunk) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, chunk)], cancellationToken);
    }

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        internal long Consumed { get; private set; }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int count = await base.ReadAsync(buffer, cancellationToken);
            Consumed += count;
            return count;
        }
    }
}
