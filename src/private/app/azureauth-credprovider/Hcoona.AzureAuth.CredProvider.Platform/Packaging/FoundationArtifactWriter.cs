using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Hcoona.AzureAuth.CredProvider.Platform.Packaging;

public static class FoundationArtifactWriter
{
    public const string ManifestArchivePath = "manifest.json";

    private static readonly DateTimeOffset DeterministicArchiveTimestamp =
        new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly JsonSerializerOptions ManifestJsonOptions = new(
        JsonSerializerDefaults.Web
    )
    {
        TypeInfoResolver = FoundationArtifactJsonSerializerContext.Default,
    };

    public static FoundationArtifactPackage Create(
        Stream destination,
        IEnumerable<FoundationArtifactInput> inputs,
        FoundationArtifactOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfMissingMetadata(options.ArtifactName, nameof(options.ArtifactName));
        ThrowIfMissingMetadata(options.BuildOs, nameof(options.BuildOs));
        ThrowIfMissingMetadata(options.TargetRid, nameof(options.TargetRid));
        ThrowIfMissingMetadata(options.ProductVersion, nameof(options.ProductVersion));
        ThrowIfMissingMetadata(options.SourceRevision, nameof(options.SourceRevision));
        FoundationArtifactPath.EnsureSafeTargetRid(options.TargetRid);

        PreparedArtifactFile[] preparedFiles = PrepareFiles(inputs);
        try
        {
            var manifest = new FoundationArtifactManifest(
                FoundationArtifactManifest.CurrentSchemaVersion,
                options.ArtifactName,
                options.BuildOs,
                options.TargetRid,
                options.ProductVersion,
                options.SourceRevision,
                "eng/scripts/azureauth-credprovider/New-FoundationArtifact.ps1",
                "internal-non-release",
                "unsigned",
                IsInternal: true,
                IsRelease: false,
                IsSigned: false,
                preparedFiles
                    .Select(file => new FoundationArtifactFile(
                        file.ArtifactPath,
                        file.Length,
                        file.Sha256
                    ))
                    .ToArray()
            );
            byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(
                manifest,
                ManifestJsonOptions
            );

            using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);
            WriteBytes(archive, ManifestArchivePath, manifestBytes);
            foreach (PreparedArtifactFile file in preparedFiles)
            {
                WriteCapturedContent(archive, file.ArtifactPath, file.Content);
            }

            return new FoundationArtifactPackage(manifest, manifestBytes);
        }
        finally
        {
            foreach (PreparedArtifactFile file in preparedFiles)
            {
                file.Dispose();
            }
        }
    }

    private static PreparedArtifactFile[] PrepareFiles(
        IEnumerable<FoundationArtifactInput> inputs
    )
    {
        var files = new List<PreparedArtifactFile>();
        var artifactPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ManifestArchivePath,
        };

        try
        {
            foreach (FoundationArtifactInput input in inputs)
            {
                ArgumentNullException.ThrowIfNull(input);
                ArgumentException.ThrowIfNullOrWhiteSpace(input.SourcePath);
                FoundationArtifactPath.EnsureSafeRelativePath(input.ArtifactPath);

                if (!artifactPaths.Add(input.ArtifactPath))
                {
                    throw new ArgumentException(
                        "Duplicate or case-ambiguous artifact path "
                            + $"'{input.ArtifactPath}' is not allowed.",
                        nameof(inputs)
                    );
                }

                files.Add(CaptureFile(input.SourcePath, input.ArtifactPath));
            }

            return files.OrderBy(file => file.ArtifactPath, StringComparer.Ordinal).ToArray();
        }
        catch
        {
            foreach (PreparedArtifactFile file in files)
            {
                file.Dispose();
            }

            throw;
        }
    }

    private static PreparedArtifactFile CaptureFile(string sourcePath, string artifactPath)
    {
        try
        {
            using FileStream source = File.OpenRead(sourcePath);
            var content = new MemoryStream();
            try
            {
                source.CopyTo(content);
                content.Position = 0;
                byte[] hash = SHA256.HashData(content);
                content.Position = 0;
                return new PreparedArtifactFile(
                    artifactPath,
                    content,
                    content.Length,
                    Convert.ToHexString(hash).ToLowerInvariant()
                );
            }
            catch
            {
                content.Dispose();
                throw;
            }
        }
        catch (Exception exception)
            when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new FileNotFoundException(
                $"Artifact source file '{sourcePath}' does not exist.",
                sourcePath
            );
        }
    }

    private static void WriteBytes(ZipArchive archive, string artifactPath, byte[] content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(artifactPath, CompressionLevel.NoCompression);
        entry.LastWriteTime = DeterministicArchiveTimestamp;
        using Stream stream = entry.Open();
        stream.Write(content);
    }

    private static void WriteCapturedContent(
        ZipArchive archive,
        string artifactPath,
        MemoryStream content
    )
    {
        ZipArchiveEntry entry = archive.CreateEntry(
            artifactPath,
            CompressionLevel.NoCompression
        );
        entry.LastWriteTime = DeterministicArchiveTimestamp;
        using Stream destination = entry.Open();
        content.Position = 0;
        content.CopyTo(destination);
    }

    private static void ThrowIfMissingMetadata(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Artifact metadata must be explicit.", parameterName);
        }
    }

    private sealed record PreparedArtifactFile(
        string ArtifactPath,
        MemoryStream Content,
        long Length,
        string Sha256
    ) : IDisposable
    {
        public void Dispose()
        {
            Content.Dispose();
        }
    }
}
