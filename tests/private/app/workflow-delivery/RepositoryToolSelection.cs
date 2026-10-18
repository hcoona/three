using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace WorkflowDelivery.Tests;

// Projects the repository's committed tool selection into native fixtures, so fixtures follow
// root configuration upgrades instead of copying their versions.
internal static class RepositoryToolSelection
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

    private static JsonObject GlobalJsonNode() => JsonNode.Parse(File.ReadAllText(
        Path.Combine(SourceRoot, "global.json")))!.AsObject();
}
