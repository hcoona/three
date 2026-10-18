using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace WorkflowDelivery.Tests;

// Reads tool and runtime selections from the committed root configuration, so tests and
// native fixtures follow a routine version update without their own copies of the versions.
internal static partial class RepositoryToolSelection
{
    private static readonly Lazy<string> Root = new(() =>
    {
        string directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "global.json")))
            directory = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(directory)) ??
                throw new InvalidDataException("Repository source root is unavailable.");
        return directory;
    });

    internal static string SourceRoot => Root.Value;

    internal static string DotNetSdkVersion =>
        GlobalJsonNode()["sdk"]!["version"]!.GetValue<string>();

    // The root SDK selection with only the named root MSBuild SDK versions.
    internal static string GlobalJson(params string[] msbuildSdks)
    {
        JsonObject root = GlobalJsonNode();
        var projection = new JsonObject { ["sdk"] = root["sdk"]!.DeepClone() };
        if (msbuildSdks.Length != 0)
        {
            JsonObject selected = root["msbuild-sdks"]!.AsObject();
            projection["msbuild-sdks"] = new JsonObject(msbuildSdks.Select(name =>
                KeyValuePair.Create(name, (JsonNode?)(selected[name]?.DeepClone() ??
                    throw new InvalidDataException("Root global.json does not select " +
                        name + ".")))));
        }
        return projection.ToJsonString();
    }

    // A root Directory.Build.props GlobalPackageReference version.
    internal static string GlobalPackageVersion(string id) => XDocument.Load(
            Path.Combine(SourceRoot, "Directory.Build.props")).Descendants()
        .Single(element => element.Name.LocalName == "GlobalPackageReference" &&
            (string?)element.Attribute("Include") == id)
        .Attribute("Version")!.Value;

    internal static string MiseToml => File.ReadAllText(Path.Combine(SourceRoot, "mise.toml"));

    internal static string MiseLock => File.ReadAllText(Path.Combine(SourceRoot, "mise.lock"));

    // The root mise.lock version of a simply named tool, such as "ruby" or "python".
    internal static string Locked(string tool) => Locked(MiseLock, tool);

    // The global.json msbuild-sdks version of one SDK, from the source root by default.
    internal static string MsBuildSdk(string name, string? root = null)
    {
        using JsonDocument global = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(root ?? SourceRoot, "global.json")));
        return global.RootElement.GetProperty("msbuild-sdks").GetProperty(name).GetString()
            ?? throw new InvalidDataException("Missing global.json SDK: " + name);
    }

    // The single version a mise.lock text records for a tool.
    internal static string Locked(string miseLock, string tool)
    {
        string[] versions = LockEntry().Matches(miseLock)
            .Where(match => match.Groups["tool"].Value == tool)
            .Select(match => match.Groups["version"].Value).Distinct(StringComparer.Ordinal)
            .ToArray();
        return versions.Length == 1 ? versions[0]
            : throw new InvalidDataException("No single locked version for " + tool + ".");
    }

    // The request a mise.toml [tools] table writes for a simply named tool.
    internal static string Requested(string miseToml, string tool)
    {
        Match match = Regex.Match(miseToml, "^" + Regex.Escape(tool) +
            "\\s*=\\s*\"(?<request>[^\"]+)\"\\s*$", RegexOptions.Multiline |
            RegexOptions.CultureInvariant);
        return match.Success ? match.Groups["request"].Value
            : throw new InvalidDataException("No committed request for " + tool + ".");
    }

    private static JsonObject GlobalJsonNode() => JsonNode.Parse(File.ReadAllText(
        Path.Combine(SourceRoot, "global.json")))!.AsObject();

    [GeneratedRegex(
        "^\\[\\[tools\\.(?<tool>[A-Za-z0-9_-]+)\\]\\]\\r?\\nversion = \"(?<version>[^\"]+)\"",
        RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex LockEntry();
}
