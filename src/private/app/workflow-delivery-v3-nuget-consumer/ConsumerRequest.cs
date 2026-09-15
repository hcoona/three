using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using NuGet.Versioning;

namespace WorkflowDeliveryV3NuGetConsumer;

internal sealed record ConsumerRequest(
    string Workspace,
    string Version,
    string PackageSha256,
    string WitnessSha256,
    string GraphSha256,
    string ServiceIndexSha256,
    string PackageBaseAddress,
    int MaximumRequests,
    long MaximumResponseBytes,
    int TimeoutSeconds
)
{
    internal const string PackageId = "Hcoona.ReleaseSmoke.GithubPackages";
    internal const string NormalizedId = "hcoona.releasesmoke.githubpackages";
    internal const string ServiceIndex = "https://nuget.pkg.github.com/hcoona/index.json";
    internal const string Schema = "workflow-delivery/v3/nuget-consumer-restore-request-v2";
    internal const string RedirectPolicy = "nuget-package-location-v1";
    internal const string HttpSchema = "workflow-delivery/v3/nuget-consumer-http-v2";
    internal const string WitnessPath = "workflow-delivery/provenance.json";
    internal const string Configuration =
        "<configuration><packageSources><clear/>"
        + "<add key=\"selected\" value=\""
        + ServiceIndex
        + "\"/>"
        + "</packageSources><packageSourceMapping><clear/>"
        + "<packageSource key=\"selected\"><package pattern=\""
        + PackageId
        + "\"/>"
        + "</packageSource></packageSourceMapping></configuration>";

    internal string ProjectPath => Path.Combine(Workspace, "consumer.csproj");
    internal string GraphPath => Path.Combine(Workspace, "graph.json");
    internal string ConfigPath => Path.Combine(Workspace, "nuget.config");
    internal string PackagesPath => Path.Combine(Workspace, "packages");
    internal string EvidencePath => Path.Combine(Workspace, "restore-evidence");
    internal string PackageUrl =>
        PackageBaseAddress
        + NormalizedId
        + "/"
        + Version
        + "/"
        + NormalizedId
        + "."
        + Version
        + ".nupkg";

    internal JsonObject ToDocument() =>
        new()
        {
            ["schema"] = Schema,
            ["packageRedirectPolicy"] = RedirectPolicy,
            ["workspace"] = Workspace,
            ["version"] = Version,
            ["packageSha256"] = PackageSha256,
            ["witnessSha256"] = WitnessSha256,
            ["graphSha256"] = GraphSha256,
            ["serviceIndexSha256"] = ServiceIndexSha256,
            ["packageBaseAddress"] = PackageBaseAddress,
            ["maximumRequests"] = MaximumRequests,
            ["maximumResponseBytes"] = MaximumResponseBytes,
            ["timeoutSeconds"] = TimeoutSeconds,
        };

    internal static ConsumerRequest Read(byte[] content)
    {
        Require(content.Length is > 0 and <= 65536, "Invalid consumer request size.");
        JsonObject value =
            JsonNode.Parse(content)?.AsObject()
            ?? throw new InvalidDataException("Missing consumer request.");
        string[] keys =
        [
            "schema",
            "packageRedirectPolicy",
            "workspace",
            "version",
            "packageSha256",
            "witnessSha256",
            "graphSha256",
            "serviceIndexSha256",
            "packageBaseAddress",
            "maximumRequests",
            "maximumResponseBytes",
            "timeoutSeconds",
        ];
        Require(
            value.Count == keys.Length
                && keys.All(value.ContainsKey)
                && Text(value, "schema") == Schema
                && Text(value, "packageRedirectPolicy") == RedirectPolicy,
            "Invalid consumer request contract."
        );
        var request = new ConsumerRequest(
            Text(value, "workspace"),
            Text(value, "version"),
            Text(value, "packageSha256"),
            Text(value, "witnessSha256"),
            Text(value, "graphSha256"),
            Text(value, "serviceIndexSha256"),
            Text(value, "packageBaseAddress"),
            value["maximumRequests"]!.GetValue<int>(),
            value["maximumResponseBytes"]!.GetValue<long>(),
            value["timeoutSeconds"]!.GetValue<int>()
        );
        request.Validate();
        return request;
    }

    internal void Validate()
    {
        Require(
            Path.IsPathFullyQualified(Workspace) && Path.GetFullPath(Workspace) == Workspace,
            "Consumer workspace must be an absolute normalized path."
        );
        NuGetVersion parsed = NuGetVersion.Parse(Version);
        string normalized = parsed.ToNormalizedString().ToLowerInvariant();
        Require(
            string.Equals(normalized, Version, StringComparison.Ordinal),
            "Consumer version must be the exact native coordinate."
        );
        foreach (
            string digest in new[] { PackageSha256, WitnessSha256, GraphSha256, ServiceIndexSha256 }
        )
        {
            Require(
                digest.Length == 64
                    && digest.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f'),
                "Invalid consumer digest."
            );
        }
        var address = new Uri(PackageBaseAddress, UriKind.Absolute);
        Require(
            address.Scheme == Uri.UriSchemeHttps
                && address.Host == "nuget.pkg.github.com"
                && address.Port == 443
                && address.UserInfo.Length == 0
                && address.Query.Length == 0
                && address.Fragment.Length == 0
                && address.AbsolutePath.StartsWith("/hcoona/", StringComparison.Ordinal)
                && address.AbsolutePath.EndsWith('/')
                && address.AbsoluteUri == PackageBaseAddress,
            "Unadmitted consumer package resource."
        );
        Require(
            MaximumRequests > 0
                && MaximumResponseBytes is > 0 and < int.MaxValue
                && TimeoutSeconds is > 0 and <= 3600,
            "Invalid consumer operation bounds."
        );
    }

    internal void ValidateGraph()
    {
        Validate();
        byte[] graphBytes = File.ReadAllBytes(GraphPath);
        Require(Sha256(graphBytes) == GraphSha256, "Consumer graph bytes changed.");
        JsonObject graph = JsonNode.Parse(graphBytes)!.AsObject();
        JsonObject projects = graph["projects"]!.AsObject();
        Require(
            graph["format"]!.GetValue<int>() == 1
                && projects.Count == 1
                && graph["restore"]!.AsObject().Count == 1
                && graph["restore"]![ProjectPath] is JsonObject
                && projects[ProjectPath] is JsonObject,
            "Unexpected consumer project closure."
        );
        JsonObject project = projects[ProjectPath]!.AsObject();
        JsonObject restore = project["restore"]!.AsObject();
        Require(
            Text(restore, "projectUniqueName") == ProjectPath
                && Text(restore, "projectPath") == ProjectPath
                && Text(restore, "projectStyle") == "PackageReference"
                && Text(restore, "packagesPath") == PackagesPath
                && Path.TrimEndingDirectorySeparator(Text(restore, "outputPath"))
                    == Path.Combine(Workspace, "obj")
                && restore["configFilePaths"]!
                    .AsArray()
                    .Select(node => node!.GetValue<string>())
                    .SequenceEqual([ConfigPath])
                && restore["sources"]!.AsObject().Count == 1
                && restore["sources"]![ServiceIndex] is JsonObject
                && restore["originalTargetFrameworks"]!
                    .AsArray()
                    .Select(node => node!.GetValue<string>())
                    .SequenceEqual(["net10.0"])
                && restore["restoreAuditProperties"]?["enableAudit"]?.GetValue<string>() == "false"
                && restore["fallbackFolders"] is null,
            "Unexpected consumer restore inputs."
        );
        JsonObject restoreFrameworks = restore["frameworks"]!.AsObject();
        Require(
            restoreFrameworks.Count == 1
                && restoreFrameworks["net10.0"]?["projectReferences"]?.AsObject().Count == 0,
            "Consumer project references are not allowed."
        );
        JsonObject frameworks = project["frameworks"]!.AsObject();
        Require(
            frameworks.Count == 1
                && frameworks["net10.0"] is JsonObject
                && project["dependencies"] is null
                && project["runtimes"] is null,
            "Unexpected consumer framework or dependencies."
        );
        JsonObject framework = frameworks["net10.0"]!.AsObject();
        JsonObject dependencies = framework["dependencies"]!.AsObject();
        Require(
            dependencies.Count == 1 && dependencies[PackageId] is JsonObject,
            "Consumer must reference only the selected package."
        );
        JsonObject dependency = dependencies[PackageId]!.AsObject();
        VersionRange range = VersionRange.Parse(Text(dependency, "version"));
        NuGetVersion expected = NuGetVersion.Parse(Version);
        Require(
            Text(dependency, "target") == "Package"
                && !range.IsFloating
                && range.IsMinInclusive
                && range.IsMaxInclusive
                && range.MinVersion == expected
                && range.MaxVersion == expected,
            "Consumer version is not exact."
        );
        Require(
            XNode.DeepEquals(XDocument.Load(ConfigPath).Root, XDocument.Parse(Configuration).Root),
            "Consumer configuration or source mapping changed."
        );
        foreach (
            string directory in new[]
            {
                PackagesPath,
                Path.Combine(Workspace, "http-cache"),
                Path.Combine(Workspace, "plugin-cache"),
            }
        )
        {
            Require(
                !Directory.Exists(directory)
                    || !Directory.EnumerateFileSystemEntries(directory).Any(),
                "Consumer cache is not empty."
            );
        }
    }

    internal void ValidateEnvironment()
    {
        foreach (
            (string key, string relative) in new[]
            {
                ("NUGET_PACKAGES", "packages"),
                ("NUGET_HTTP_CACHE_PATH", "http-cache"),
                ("NUGET_PLUGINS_CACHE_PATH", "plugin-cache"),
                ("DOTNET_CLI_HOME", "home"),
            }
        )
        {
            Require(
                Environment.GetEnvironmentVariable(key) == Path.Combine(Workspace, relative),
                "Consumer process cache or home environment is not isolated."
            );
        }
    }

    internal static string Sha256(ReadOnlySpan<byte> content) =>
        Convert.ToHexStringLower(SHA256.HashData(content));

    internal static string Text(JsonObject value, string key) =>
        value[key]?.GetValue<string>()
        ?? throw new InvalidDataException("Missing consumer text input.");

    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
