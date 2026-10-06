using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Xml.Linq;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Versioning;

namespace WorkflowDelivery.CI;

internal sealed record DotNetOriginalPackage(string Unit, string Build, string Output,
    string Role, string Kind, string Path, long Size, string Sha256);

// Read the registered product's normal package with NuGet and .NET metadata APIs.
internal static class DotNetPackageArchive
{
    private const int StreamLimit = 8 * 1024 * 1024;

    internal static PackageIdentity Identity(string path)
    {
        using var reader = new PackageArchiveReader(path);
        Names(reader);
        using MemoryStream nuspec = Read(reader, reader.GetNuspecFile());
        return new NuspecReader(nuspec).GetIdentity();
    }

    internal static void Contents(string path, Dictionary<string, string> expected)
    {
        using var reader = new PackageArchiveReader(path);
        Names(reader);
        using MemoryStream nuspecStream = Read(reader, reader.GetNuspecFile());
        var nuspec = new NuspecReader(nuspecStream);
        RequireIdentity(nuspec.GetIdentity(), expected);
        if (nuspec.GetDependencyGroups().Any(group => group.Packages.Any()))
            throw new InvalidDataException("The product package cannot have runtime dependencies.");
        FrameworkSpecificGroup[] groups = reader.GetLibItems().ToArray();
        NuGetFramework framework = NuGetFramework.ParseFolder(expected["TargetFramework"]);
        if (groups is not [FrameworkSpecificGroup group] ||
            !group.TargetFramework.Equals(framework))
            throw new InvalidDataException("Different or missing native package library group.");
        string assembly = Single(expected["AssemblyName"] + ".dll");
        string documentation = Single(Path.GetFileName(expected["DocumentationFile"]
            .Replace('\\', '/')));
        using (MemoryStream dll = Read(reader, assembly)) Versions(dll, expected);
        using (MemoryStream xml = Read(reader, documentation))
            if (XDocument.Load(xml).Root?.Element("assembly")?.Element("name")?.Value !=
                expected["AssemblyName"])
                throw new InvalidDataException("Missing or different native XML documentation.");
        string? readme = nuspec.GetReadme();
        if (readme != expected["PackageReadmeFile"])
            throw new InvalidDataException("Different declared native package README.");
        using (MemoryStream content = Read(reader, readme))
            if (content.Length == 0)
                throw new InvalidDataException("The declared package README is empty.");
        if (expected["PublishRepositoryUrl"].Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            RepositoryMetadata? repository = nuspec.GetRepositoryMetadata();
            if (repository is null || repository.Url != expected["RepositoryUrl"] ||
                repository.Type != expected["RepositoryType"] ||
                (repository.Commit ?? "") != expected["RepositoryCommit"])
                throw new InvalidDataException("Missing or different native repository metadata.");
        }

        string Single(string name)
        {
            string[] matches = group.Items.Where(item => Path.GetFileName(item) == name).ToArray();
            return matches is [string match] ? match :
                throw new InvalidDataException("Missing or ambiguous required package payload: " +
                    name);
        }
    }

    internal static void RequireIdentity(PackageIdentity actual,
        Dictionary<string, string> expected)
    {
        var native = new PackageIdentity(expected["PackageId"],
            NuGetVersion.Parse(expected["NuGetPackageVersion"]));
        if (!PackageIdentityComparer.Default.Equals(actual, native))
            throw new InvalidDataException("Package identity differs from native NBGV projection.");
    }

    internal static string Digest(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(file));
    }

    internal static void RequireOriginal(DotNetOriginalPackage original)
    {
        if (!File.Exists(original.Path) || new FileInfo(original.Path).Length != original.Size ||
            Digest(original.Path) != original.Sha256)
            throw new InvalidDataException("The original native package bytes are unavailable.");
    }

    private static void Names(PackageArchiveReader reader)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // GetFiles performs NuGet's decoding; no application ZIP decoder is involved.
        foreach (string name in reader.GetFiles())
            if (name.Contains('\\') || name.Contains(':') || name.Contains('\0') ||
                name.Split('/').Any(part => part is "" or "." or "..") || !names.Add(name))
                throw new InvalidDataException("Unsafe or ambiguous native-decoded package name.");
    }

    private static MemoryStream Read(PackageArchiveReader reader, string path)
    {
        using Stream stream = reader.GetStream(path);
        var buffer = new MemoryStream();
        try
        {
            byte[] block = new byte[8192];
            int count;
            while ((count = stream.Read(block)) != 0)
            {
                if (buffer.Length + count > StreamLimit)
                    throw new InvalidDataException("Required package stream exceeds its limit.");
                buffer.Write(block, 0, count);
            }
            buffer.Position = 0;
            return buffer;
        }
        catch { buffer.Dispose(); throw; }
    }

    private static void Versions(Stream stream, Dictionary<string, string> expected)
    {
        using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);
        MetadataReader metadata = pe.GetMetadataReader();
        AssemblyDefinition assembly = metadata.GetAssemblyDefinition();
        if (metadata.GetString(assembly.Name) != expected["AssemblyName"] ||
            assembly.Version != Version.Parse(expected["AssemblyVersion"]))
            throw new InvalidDataException("Different native package assembly identity/version.");
        var versions = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (CustomAttributeHandle handle in assembly.GetCustomAttributes())
        {
            CustomAttribute attribute = metadata.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
            MemberReference constructor = metadata.GetMemberReference(
                (MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference) continue;
            TypeReference type = metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            if (metadata.GetString(type.Namespace) != "System.Reflection") continue;
            string? property = metadata.GetString(type.Name) switch
            {
                "AssemblyFileVersionAttribute" => "AssemblyFileVersion",
                "AssemblyInformationalVersionAttribute" => "AssemblyInformationalVersion",
                _ => null,
            };
            if (property is null) continue;
            BlobReader value = metadata.GetBlobReader(attribute.Value);
            if (value.ReadUInt16() != 1 || !versions.TryAdd(property, value.ReadSerializedString()))
                throw new InvalidDataException("Conflicting native assembly version attributes.");
        }
        if (versions.Count != 2 || versions.Any(pair => pair.Value != expected[pair.Key]))
            throw new InvalidDataException(
                "Missing or different native assembly version attributes.");
    }
}
