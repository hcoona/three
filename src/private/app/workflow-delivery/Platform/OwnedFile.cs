using System.Security.Cryptography;

namespace WorkflowDelivery.Platform;

// Size and digest facts of a file as it is actually read. A link's own FileInfo.Length is not its
// target's, so bounds, recorded sizes, and digests are taken from the opened stream.
internal static class OwnedFile
{
    internal static void RequireExists(string path,
        string message = "Required native output is missing or not a file.")
    {
        if (!File.Exists(path)) throw new InvalidDataException(message);
    }

    internal static string Digest(string path)
    {
        using FileStream file = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(file));
    }

    internal static (long Size, string Sha256) Measure(string path,
        string missingMessage = "Required native output is missing or not a file.",
        string? emptyMessage = null)
    {
        RequireExists(path, missingMessage);
        using FileStream file = File.OpenRead(path);
        if (emptyMessage is not null && file.Length <= 0)
            throw new InvalidDataException(emptyMessage);
        return (file.Length, Convert.ToHexStringLower(SHA256.HashData(file)));
    }

    internal static async Task<(long Size, string Sha256)> MeasureAsync(string path,
        CancellationToken token,
        string missingMessage = "Required native output is missing or not a file.",
        string? emptyMessage = null)
    {
        RequireExists(path, missingMessage);
        using FileStream file = File.OpenRead(path);
        if (emptyMessage is not null && file.Length <= 0)
            throw new InvalidDataException(emptyMessage);
        return (file.Length, Convert.ToHexStringLower(await SHA256.HashDataAsync(file, token)));
    }

    internal static bool Matches(string path, long expectedSize, string expectedSha256)
    {
        if (!File.Exists(path)) return false;
        using FileStream file = File.OpenRead(path);
        return file.Length == expectedSize &&
            Convert.ToHexStringLower(SHA256.HashData(file)) == expectedSha256;
    }

    internal static void Verify(string path, long expectedSize, string expectedSha256,
        string mismatchMessage)
    {
        if (!Matches(path, expectedSize, expectedSha256))
            throw new InvalidDataException(mismatchMessage);
    }

    internal static async Task VerifyAsync(string path, long expectedSize, string expectedSha256,
        string mismatchMessage, CancellationToken token)
    {
        RequireExists(path, mismatchMessage);
        using FileStream file = File.OpenRead(path);
        if (file.Length != expectedSize ||
            Convert.ToHexStringLower(await SHA256.HashDataAsync(file, token)) != expectedSha256)
            throw new InvalidDataException(mismatchMessage);
    }
}
