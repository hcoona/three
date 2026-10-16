using System.Security.Cryptography;

namespace WorkflowDelivery.Destinations;

internal sealed record RegistryOriginalBytes(long? Size, string? Sha256, byte[]? Bytes);

// Response body readers shared by registry observations. Metadata bodies are parsed in
// memory, so they keep a fixed bound. A served original is compared with the qualified
// original by a streaming SHA-256; reading stops one byte past the original's size, since
// a longer body cannot be the same file. Registries own their package size limits.
internal static class RegistryBody
{
    internal static async Task<byte[]> LimitedAsync(Stream stream, long? declared, int limit,
        CancellationToken token)
    {
        if (declared > limit) throw new IOException("Response exceeds its body limit.");
        using var body = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) != 0)
        {
            if (body.Length + count > limit)
                throw new IOException("Response exceeds its body limit.");
            await body.WriteAsync(buffer.AsMemory(0, count), token);
        }
        if (declared is not null && body.Length != declared)
            throw new IOException("Incomplete response body.");
        return body.ToArray();
    }

    // A body longer than the original yields no size or digest: its remaining bytes are
    // neither read nor retained. Retained bytes never exceed the original's size.
    internal static async Task<RegistryOriginalBytes> OriginalAsync(Stream stream,
        long? declared, long expected, bool retain, CancellationToken token)
    {
        if (declared > expected) return new(null, null, null);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using MemoryStream? copy = retain ? new() : null;
        byte[] buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            int count = await stream.ReadAsync(buffer.AsMemory(0,
                (int)Math.Min(buffer.Length, expected + 1 - total)), token);
            if (count == 0) break;
            total += count;
            if (total > expected) return new(null, null, null);
            hash.AppendData(buffer, 0, count);
            copy?.Write(buffer, 0, count);
        }
        if (declared is not null && total != declared)
            throw new IOException("Incomplete response body.");
        return new(total, Convert.ToHexStringLower(hash.GetHashAndReset()), copy?.ToArray());
    }
}
