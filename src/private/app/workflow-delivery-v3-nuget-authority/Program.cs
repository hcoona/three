using System.Text;
using System.Text.Json;
using System.Xml;
using NuGet.Common;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.ProjectModel;

namespace WorkflowDeliveryV3NuGetAuthority;

internal static class Program
{
    private const string RequestSchema =
        "workflow-delivery/v3/static-reference-nuget-authority-request";
    private const string ResponseSchema =
        "workflow-delivery/v3/static-reference-nuget-authority-response";
    private const string GraphId = "nuget-lock-v1";

    private static async Task<int> Main()
    {
        try
        {
            AuthorityRequest request = await ReadRequestAsync().ConfigureAwait(false);
            IReadOnlyList<AuthorityFact> facts;
            if (request.Family == "nuget-lock")
            {
                try
                {
                    EnsureStrictUtf8(request.Content);
                }
                catch (DecoderFallbackException)
                {
                    WriteError("encoding-rejected");
                    return 0;
                }
            }

            try
            {
                facts = request.Family switch
                {
                    "nuget-lock" => ReadLockFacts(request),
                    "nuget-packages-config" => ReadPackagesConfigFacts(request),
                    _ => throw new InvalidOperationException("Unsupported admitted family."),
                };
            }
            catch (Exception error) when (IsAuthorityRejection(error))
            {
                WriteError("authority-rejected");
                return 0;
            }
            catch (AuthorityRejectedException)
            {
                WriteError("authority-rejected");
                return 0;
            }
            catch (AuthorityProjectionException)
            {
                WriteError("unsupported-projection");
                return 0;
            }

            WriteFacts(facts);
            return 0;
        }
        catch (Exception error)
        {
            string diagnostic =
                Environment.GetEnvironmentVariable("WDV3_STATIC_REFERENCE_DEBUG") == "1"
                    ? error.ToString()
                    : "static-reference NuGet authority execution failed";
            await Console.Error.WriteLineAsync(diagnostic).ConfigureAwait(false);
            return 1;
        }
    }

    private static bool IsAuthorityRejection(Exception error)
    {
        return error is ArgumentException
            or FormatException
            or InvalidDataException
            or PackagingException
            or XmlException;
    }

    private static async Task<AuthorityRequest> ReadRequestAsync()
    {
        using JsonDocument document = await JsonDocument
            .ParseAsync(Console.OpenStandardInput())
            .ConfigureAwait(false);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Invalid NuGet authority request.");
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                throw new InvalidDataException("Duplicate NuGet authority request field.");
            }
        }

        string[] expected = ["contentBase64", "family", "logicalPath", "schema"];
        if (!names.SetEquals(expected))
        {
            throw new InvalidDataException("Invalid NuGet authority request fields.");
        }

        string schema = RequiredString(root, "schema");
        string family = RequiredString(root, "family");
        string logicalPath = RequiredString(root, "logicalPath");
        string contentBase64 = RequiredString(root, "contentBase64");
        if (schema != RequestSchema
            || family is not ("nuget-lock" or "nuget-packages-config")
            || !IsNormalizedLogicalPath(logicalPath))
        {
            throw new InvalidDataException("Invalid NuGet authority request.");
        }

        byte[] content = Convert.FromBase64String(contentBase64);
        return new AuthorityRequest(family, logicalPath, content);
    }

    private static string RequiredString(JsonElement root, string name)
    {
        JsonElement value = root.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidDataException("Invalid NuGet authority request value.");
        }

        return value.GetString()
            ?? throw new InvalidDataException("Invalid NuGet authority request value.");
    }

    private static void EnsureStrictUtf8(byte[] content)
    {
        _ = new UTF8Encoding(
            encoderShouldEmitUTF8Identifier: false,
            throwOnInvalidBytes: true).GetCharCount(content);
    }

    private static bool IsNormalizedLogicalPath(string value)
    {
        if (string.IsNullOrEmpty(value)
            || value.StartsWith('/'))
        {
            return false;
        }

        string[] parts = value.Split('/');
        return parts.All(part => part.Length > 0 && part is not "." and not "..");
    }

    private static List<AuthorityFact> ReadLockFacts(AuthorityRequest request)
    {
        using var stream = new MemoryStream(request.Content, writable: false);
        ILogger logger = NullLogger.Instance;
        PackagesLockFile model = PackagesLockFileFormat.Read(
            stream,
            logger,
            request.LogicalPath);
        if (model is null || model.Version is < 1 or > 3)
        {
            throw new AuthorityRejectedException();
        }

        if (model.Targets is null)
        {
            throw new AuthorityProjectionException();
        }

        var facts = new List<AuthorityFact>();
        foreach (PackagesLockFileTarget target in model.Targets.OrderBy(
            item => Required(item.Name),
            StringComparer.Ordinal))
        {
            if (target.Dependencies is null)
            {
                throw new AuthorityProjectionException();
            }

            foreach (LockFileDependency dependency in target.Dependencies.OrderBy(
                item => Required(item.Id),
                DependencyIdComparer.Instance))
            {
                if (dependency.Dependencies is null)
                {
                    throw new AuthorityProjectionException();
                }

                var edges = dependency.Dependencies
                    .OrderBy(
                        item => Required(item.Id),
                        DependencyIdComparer.Instance)
                    .Select(edge => new DependencyEdge(
                        Required(edge.Id),
                        edge.VersionRange?.ToNormalizedString()))
                    .ToArray();
                facts.Add(new LockDependencyFact(
                    Required(target.Name),
                    Required(dependency.Id),
                    dependency.Type.ToString(),
                    dependency.RequestedVersion?.ToNormalizedString(),
                    dependency.ResolvedVersion?.ToNormalizedString(),
                    edges));
            }
        }

        return facts;
    }

    private static AuthorityFact[] ReadPackagesConfigFacts(
        AuthorityRequest request)
    {
        using var stream = new MemoryStream(request.Content, writable: false);
        var reader = new PackagesConfigReader(stream, leaveStreamOpen: false);
        PackageReference[] packages = reader
            .GetPackages(allowDuplicatePackageIds: false)
            .OrderBy(package => package.PackageIdentity, PackageIdentity.Comparer)
            .ToArray();
        return packages
            .Select(package =>
            {
                PackageIdentity identity = package.PackageIdentity
                    ?? throw new AuthorityProjectionException();
                return (AuthorityFact)new PackagesConfigFact(
                    Required(identity.Id),
                    identity.Version?.ToNormalizedString()
                        ?? throw new AuthorityProjectionException());
            })
            .ToArray();
    }

    private static string Required(string? value)
    {
        return !string.IsNullOrEmpty(value)
            ? value
            : throw new AuthorityProjectionException();
    }

    private static string[] ImplementationIdentities()
    {
        string projectModelVersion = AssemblyVersion(typeof(PackagesLockFileFormat));
        string packagingVersion = AssemblyVersion(typeof(PackagesConfigReader));
        return
        [
            $"NuGet.Packaging@{packagingVersion}",
            $"NuGet.ProjectModel@{projectModelVersion}",
            $"dotnet-runtime@{Environment.Version}",
        ];
    }

    private static string AssemblyVersion(Type type)
    {
        Version version = type.Assembly.GetName().Version
            ?? throw new InvalidOperationException("Authority assembly has no version.");
        return version.ToString(3);
    }

    private static void WriteFacts(IReadOnlyList<AuthorityFact> facts)
    {
        using var writer = new Utf8JsonWriter(Console.OpenStandardOutput());
        writer.WriteStartObject();
        writer.WriteString("schema", ResponseSchema);
        writer.WriteString("result", "facts");
        writer.WriteString("graph", GraphId);
        WriteImplementationIdentities(writer);
        writer.WritePropertyName("facts");
        writer.WriteStartArray();
        foreach (AuthorityFact fact in facts)
        {
            fact.Write(writer);
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.Flush();
    }

    private static void WriteError(string errorKind)
    {
        using var writer = new Utf8JsonWriter(Console.OpenStandardOutput());
        writer.WriteStartObject();
        writer.WriteString("schema", ResponseSchema);
        writer.WriteString("result", "error");
        writer.WriteString("graph", GraphId);
        WriteImplementationIdentities(writer);
        writer.WriteString("errorKind", errorKind);
        writer.WriteEndObject();
        writer.Flush();
    }

    private static void WriteImplementationIdentities(Utf8JsonWriter writer)
    {
        writer.WritePropertyName("implementationIdentities");
        writer.WriteStartArray();
        foreach (string identity in ImplementationIdentities().Order(StringComparer.Ordinal))
        {
            writer.WriteStringValue(identity);
        }

        writer.WriteEndArray();
    }

    private sealed record AuthorityRequest(
        string Family,
        string LogicalPath,
        byte[] Content);

    private abstract record AuthorityFact
    {
        public abstract void Write(Utf8JsonWriter writer);
    }

    private sealed record DependencyEdge(
        string Id,
        string? RequestedRange);

    private sealed record LockDependencyFact(
        string Target,
        string Id,
        string DependencyType,
        string? RequestedRange,
        string? ResolvedVersion,
        IReadOnlyList<DependencyEdge> Dependencies) : AuthorityFact
    {
        public override void Write(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", "nuget-lock-dependency");
            writer.WriteString("target", Target);
            writer.WriteString("id", Id);
            writer.WriteString("dependencyType", DependencyType);
            WriteNullableString(writer, "requestedRange", RequestedRange);
            WriteNullableString(writer, "resolvedVersion", ResolvedVersion);
            writer.WritePropertyName("dependencies");
            writer.WriteStartArray();
            foreach (DependencyEdge edge in Dependencies)
            {
                writer.WriteStartObject();
                writer.WriteString("id", edge.Id);
                WriteNullableString(writer, "requestedRange", edge.RequestedRange);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }
    }

    private sealed record PackagesConfigFact(
        string Id,
        string Version) : AuthorityFact
    {
        public override void Write(Utf8JsonWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteString("kind", "nuget-packages-config-entry");
            writer.WriteString("id", Id);
            writer.WriteString("version", Version);
            writer.WriteEndObject();
        }
    }

    private static void WriteNullableString(
        Utf8JsonWriter writer,
        string name,
        string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    private sealed class DependencyIdComparer : IComparer<string>
    {
        public static DependencyIdComparer Instance { get; } = new();

        public int Compare(string? left, string? right)
        {
            int insensitive = StringComparer.OrdinalIgnoreCase.Compare(left, right);
            return insensitive != 0
                ? insensitive
                : StringComparer.Ordinal.Compare(left, right);
        }
    }

    private sealed class AuthorityProjectionException : Exception;

    private sealed class AuthorityRejectedException : Exception;
}
