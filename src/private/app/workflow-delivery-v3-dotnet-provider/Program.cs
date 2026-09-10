using System.Text.Json.Nodes;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using Newtonsoft.Json.Linq;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.Protocol;
using NuGet.Versioning;

namespace WorkflowDeliveryV3DotnetProvider;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            JsonObject result = args switch
            {
                ["normalize-identity", string id, string version] => Identity(id, version),
                ["inspect-package", string path] => InspectPackage(path),
                ["service-resources", string path] => ServiceResources(path),
                ["audit-binlog", string path] => AuditBinlog(path),
                _ => throw new ArgumentException("Unsupported native helper operation."),
            };
            Console.WriteLine(result.ToJsonString());
            return 0;
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Console.Error.WriteLine(
                "Native NuGet helper rejected the input: " + error.GetType().Name);
            return 1;
        }
    }

    private static JsonObject Identity(string id, string version)
    {
        if (!PackageIdValidator.IsValidPackageId(id))
        {
            throw new ArgumentException("Invalid native package ID.");
        }

        NuGetVersion parsed = NuGetVersion.Parse(version);
        var identity = new PackageIdentity(id, parsed);
        return new JsonObject
        {
            ["displayPackageId"] = identity.Id,
            ["displayVersion"] = version,
            ["normalizedPackageId"] = identity.Id.ToLowerInvariant(),
            ["normalizedVersion"] = identity.Version.ToNormalizedString().ToLowerInvariant(),
        };
    }

    private static JsonObject InspectPackage(string path)
    {
        using var reader = new PackageArchiveReader(path);
        PackageIdentity identity = reader.GetIdentity();
        string[] files = reader.GetFiles().Order(StringComparer.Ordinal).ToArray();
        if (files.Distinct(StringComparer.OrdinalIgnoreCase).Count() != files.Length
            || files.Any(file => file.StartsWith('/') || file.Contains('\\')
                || file.Split('/').Any(part => part is ".." or "." or "")))
        {
            throw new InvalidDataException("Package entry closure is invalid.");
        }

        byte[] witness = [];
        if (files.Contains("workflow-delivery/provenance.json", StringComparer.Ordinal))
        {
            using Stream stream = reader.GetStream("workflow-delivery/provenance.json");
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            witness = buffer.ToArray();
        }

        var dependencies = new JsonArray();
        foreach (PackageDependencyGroup group in reader.GetPackageDependencies())
        {
            foreach (PackageDependency dependency in group.Packages)
            {
                dependencies.Add(new JsonObject
                {
                    ["framework"] = group.TargetFramework.GetShortFolderName(),
                    ["id"] = dependency.Id,
                    ["versionRange"] = dependency.VersionRange.ToNormalizedString(),
                });
            }
        }

        RepositoryMetadata repository = reader.NuspecReader.GetRepositoryMetadata();
        string[] assemblies = files.Where(file => file.StartsWith("lib/", StringComparison.Ordinal)
            && file.EndsWith(".dll", StringComparison.Ordinal)).ToArray();
        return new JsonObject
        {
            ["identity"] = Identity(identity.Id,
                identity.Version.OriginalVersion ?? identity.Version.ToFullString()),
            ["witnessBase64"] = Convert.ToBase64String(witness),
            ["entries"] = Strings(files),
            ["frameworks"] = Strings(reader.GetLibItems()
                .Select(group => group.TargetFramework.GetShortFolderName())
                .Order(StringComparer.Ordinal)),
            ["dependencies"] = dependencies,
            ["repository"] = new JsonObject
            {
                ["url"] = repository.Url,
                ["commit"] = repository.Commit,
            },
            ["assembly"] = assemblies.Length == 1 ? InspectAssembly(reader, assemblies[0]) : null,
        };
    }

    private static JsonObject InspectAssembly(PackageArchiveReader package, string path)
    {
        using Stream source = package.GetStream(path);
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        buffer.Position = 0;
        using var pe = new PEReader(buffer);
        MetadataReader metadata = pe.GetMetadataReader();
        AssemblyDefinition assembly = metadata.GetAssemblyDefinition();
        var result = new JsonObject
        {
            ["name"] = metadata.GetString(assembly.Name),
            ["version"] = assembly.Version.ToString(),
        };
        foreach (CustomAttributeHandle handle in assembly.GetCustomAttributes())
        {
            CustomAttribute attribute = metadata.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference)
            {
                continue;
            }

            MemberReference constructor = metadata.GetMemberReference(
                (MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference)
            {
                continue;
            }

            TypeReference type = metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            if (metadata.GetString(type.Namespace) != "System.Reflection")
            {
                continue;
            }

            string? key = metadata.GetString(type.Name) switch
            {
                "AssemblyFileVersionAttribute" => "fileVersion",
                "AssemblyInformationalVersionAttribute" => "informationalVersion",
                _ => null,
            };
            if (key is not null)
            {
                BlobReader value = metadata.GetBlobReader(attribute.Value);
                if (value.ReadUInt16() != 1 || result.ContainsKey(key))
                {
                    throw new InvalidDataException("Conflicting assembly version attributes.");
                }

                result[key] = value.ReadSerializedString();
            }
        }

        return result;
    }

    private static JsonObject ServiceResources(string path)
    {
        var resource = new ServiceIndexResourceV3(
            JObject.Parse(File.ReadAllText(path)), DateTime.UtcNow);
        Uri[] packageBase = resource.GetServiceEntryUris("PackageBaseAddress/3.0.0")
            .Distinct().ToArray();
        Uri[] packagePublish = resource.GetServiceEntryUris("PackagePublish/2.0.0")
            .Distinct().ToArray();
        if (packageBase.Length != 1 || packagePublish.Length != 1)
        {
            throw new InvalidDataException(
                "Expected one supported package base and publication resource.");
        }

        return new JsonObject
        {
            ["packageBaseAddress"] = packageBase[0].AbsoluteUri,
            ["packagePublish"] = packagePublish[0].AbsoluteUri,
        };
    }

    private static JsonObject AuditBinlog(string path)
    {
        var tasks = new SortedSet<string>(StringComparer.Ordinal);
        var imports = new SortedSet<string>(StringComparer.Ordinal);
        var errors = new List<string>();
        bool completed = false;
        bool succeeded = false;
        var replay = new BinaryLogReplayEventSource();
        replay.TaskStarted += (_, item) => tasks.Add(item.TaskName);
        replay.ErrorRaised += (_, item) => errors.Add(item.Message ?? item.Code);
        replay.AnyEventRaised += (_, item) =>
        {
            if (item is ProjectImportedEventArgs imported && !imported.ImportIgnored
                && !string.IsNullOrEmpty(imported.ImportedProjectFile))
            {
                imports.Add(imported.ImportedProjectFile);
            }
        };
        replay.BuildFinished += (_, item) => { completed = true; succeeded = item.Succeeded; };
        replay.Replay(path);
        return new JsonObject
        {
            ["completed"] = completed,
            ["succeeded"] = succeeded,
            ["tasks"] = Strings(tasks),
            ["imports"] = Strings(imports),
            ["errors"] = Strings(errors),
            ["nbgvExecuted"] = tasks.Any(task =>
                task.Contains("Nerdbank", StringComparison.OrdinalIgnoreCase)
                || task.Contains("GetBuildVersion", StringComparison.OrdinalIgnoreCase)),
        };
    }

    private static JsonArray Strings(IEnumerable<string> values) =>
        new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
}
