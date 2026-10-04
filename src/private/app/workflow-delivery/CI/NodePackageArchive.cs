using System.Formats.Tar;
using System.IO.Compression;

namespace WorkflowDelivery.CI;

// Inspect required members without extracting archive-controlled paths. Native PNPM
// remains responsible for installing the original archive and package-format semantics.
internal static class NodePackageArchive
{
    internal const long CompressedLimit = 32 * 1024 * 1024;
    internal const long ExpandedLimit = 128 * 1024 * 1024;
    internal const long MemberLimit = 32 * 1024 * 1024;
    internal const int MemberCountLimit = 1024;
    internal const int ManifestLimit = 1024 * 1024;

    internal static async Task ReadAsync(string archive, string metadataDirectory,
        CancellationToken token)
    {
        RequireRegularFile(archive);
        if (new FileInfo(archive).Length is <= 0 or > CompressedLimit)
            throw new InvalidDataException("Native npm archive exceeds its compressed size bound.");
        if (!Directory.Exists(metadataDirectory) ||
            Directory.EnumerateFileSystemEntries(metadataDirectory).Any())
            throw new InvalidDataException("Packed metadata requires a fresh empty directory.");
        using var input = File.OpenRead(archive);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var bounded = new BoundedReadStream(gzip, ExpandedLimit);
        using var tar = new TarReader(bounded, leaveOpen: true);
        bool manifest = false;
        bool payload = false;
        int count = 0;
        while (await tar.GetNextEntryAsync(copyData: false, token) is { } entry)
        {
            if (++count > MemberCountLimit || entry.Length > MemberLimit)
                throw new InvalidDataException("Native npm archive exceeds its member bounds.");
            if (entry.Name is "package/binding.gyp" or "package/.hooks" ||
                entry.Name.StartsWith("package/.hooks/", StringComparison.Ordinal))
                throw new InvalidDataException(
                    "Unsupported npm native-build member: " + entry.Name);
            if (entry.Name is not ("package/package.json" or "package/dist/index.js"))
                continue;
            bool isManifest = entry.Name == "package/package.json";
            if ((isManifest ? manifest : payload) ||
                entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) ||
                entry.Length is <= 0 or > ManifestLimit || entry.DataStream is null)
                throw new InvalidDataException("Missing, ambiguous or unsupported npm member: "
                    + entry.Name);
            // The unseekable stream must be consumed before requesting the next entry.
            if (isManifest)
            {
                using var destination = new FileStream(Path.Combine(metadataDirectory,
                    "package.json"), FileMode.CreateNew, FileAccess.Write);
                await entry.DataStream.CopyToAsync(destination, token);
                manifest = true;
            }
            else
            {
                await entry.DataStream.CopyToAsync(Stream.Null, token);
                payload = true;
            }
        }
        await bounded.CopyToAsync(Stream.Null, token);
        if (!manifest || !payload)
            throw new InvalidDataException(
                "The npm archive lacks its required manifest/ESM payload.");
    }

    internal static void RequireRegularFile(string path)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0)
            throw new InvalidDataException("Required npm output is not a regular file: " + path);
    }

    private sealed class BoundedReadStream(Stream input, long limit) : Stream
    {
        private long consumed;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int count = input.Read(buffer);
            Account(count);
            return count;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            int count = await input.ReadAsync(buffer, cancellationToken);
            Account(count);
            return count;
        }

        private void Account(int count)
        {
            consumed += count;
            if (consumed > limit)
                throw new InvalidDataException(
                    "Native npm archive exceeds its expanded size bound.");
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) =>
            throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();
    }
}
