using System.Text.Json.Serialization;
using static WorkflowDelivery.Repository.StrictYaml;

namespace WorkflowDelivery.Repository;

internal sealed record ReleaseOutput(string Id, string Role, string Kind);

[JsonDerivedType(typeof(NuGetQualification), "nuget")]
[JsonDerivedType(typeof(NpmQualification), "npm")]
[JsonDerivedType(typeof(PythonQualification), "python")]
[JsonDerivedType(typeof(RubyQualification), "ruby")]
[JsonDerivedType(typeof(WindowsAssetsQualification), "windows-assets")]
[JsonDerivedType(typeof(BrowserExtensionQualification), "browser-extension")]
[JsonDerivedType(typeof(InventoryQualification), "unregistered")]
internal abstract record BuildQualification;
internal sealed record NuGetQualification(string Probe, string? Declarations, string[] Analyzers)
    : BuildQualification;
internal sealed record NpmQualification(string Probe, bool TestBeforeBuild) : BuildQualification;
internal sealed record PythonQualification(string[] Api, string? Tests, bool StaticVersion,
    bool RuntimeClosure, string[] Dependencies) : BuildQualification;
internal sealed record RubyQualification(string Probe, string? Require, bool NbgvEnv,
    bool LockedBundle, bool RSpec, string[] Dependencies) : BuildQualification;
internal sealed record WindowsAssetsQualification(string AssetPrefix, string Executable,
    string Editor) : BuildQualification;
internal sealed record BrowserExtensionQualification(string ManifestName) : BuildQualification;
// Unknown definitions can be inventoried for comparison; they are not executable recipes.
internal sealed record InventoryQualification(
    Dictionary<string, string[]> Fields) : BuildQualification;

internal sealed record ReleaseBuild(
    string Id,
    string Definition,
    string EntryPoint,
    ReleaseOutput[] Outputs,
    BuildQualification? Qualification = null);

internal sealed record ReleaseUnitDeclaration(
    string SourcePath,
    string Id,
    ReleaseBuild[] Builds,
    string[]? Channels = null)
{
    private static readonly string[] DefaultChannels = ["buddy", "official"];
    internal string[] EffectiveChannels => Channels ?? DefaultChannels;

    internal bool AllowsChannel(string channel, bool dryRun) =>
        EffectiveChannels.Contains(channel, StringComparer.Ordinal) ||
        dryRun && EffectiveChannels.Contains(channel + "-dry-run", StringComparer.Ordinal);
}

// The caller obtains exact committed content; parsing supplied text cannot establish its revision.
internal static class ReleaseUnitDeclarationReader
{
    private static readonly HashSet<string> AllowedChannels = new(StringComparer.Ordinal)
    {
        "buddy", "buddy-dry-run", "official", "official-dry-run",
    };

    private static readonly string[] BuildAllowedKeys =
        ["id", "definition", "entry-point", "outputs", "qualification"];

    internal static ReleaseUnitDeclaration Read(string content, string sourcePath)
    {
        ArgumentNullException.ThrowIfNull(content);
        string path = RelativePath(sourcePath, allowDirectoryRoot: false);
        const string declaration = "release-unit";
        Dictionary<string, object?> unit = MappingWithOptional(Parse(content, declaration, path),
            declaration, ["schema", "release-unit", "builds"],
            ["schema", "release-unit", "builds", "channels"]);
        if (Text(unit["schema"]) != "workflow-delivery/v3/release-unit")
            throw new InvalidDataException("Invalid release-unit schema.");

        string id = Identity(unit["release-unit"]);
        string[]? channels = null;
        if (unit.TryGetValue("channels", out object? channelsNode))
        {
            channels = [.. Sequence(channelsNode).Select(Text)];
            if (channels.Length == 0 ||
                channels.Distinct(StringComparer.Ordinal).Count() != channels.Length ||
                channels.Any(ch => !AllowedChannels.Contains(ch)))
                throw new InvalidDataException("Invalid release-unit channels.");
        }

        var builds = new List<ReleaseBuild>();
        var buildIds = new HashSet<string>(StringComparer.Ordinal);
        var outputIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (object? value in Sequence(unit["builds"]))
        {
            Dictionary<string, object?> build = MappingWithOptional(value, declaration,
                ["id", "definition", "entry-point", "outputs"], BuildAllowedKeys);
            string buildId = Identity(build["id"]);
            if (!buildIds.Add(buildId))
                throw new InvalidDataException("Duplicate release build identity.");
            string definition = Text(build["definition"]);
            string entryPoint = RelativePath(build["entry-point"], allowDirectoryRoot: true);
            var outputs = new List<ReleaseOutput>();
            foreach (object? outputValue in Sequence(build["outputs"]))
            {
                Dictionary<string, object?> output = Mapping(outputValue, declaration, "id",
                    "role", "kind");
                string outputId = Identity(output["id"]);
                if (!outputIds.Add(outputId))
                    throw new InvalidDataException("Duplicate release output identity.");
                outputs.Add(new(outputId, Identity(output["role"]), Identity(output["kind"])));
            }
            BuildQualification? qualification = build.TryGetValue("qualification",
                out object? qualNode) ? ParseQualification(qualNode, definition) : null;
            builds.Add(new(buildId, definition, entryPoint, [.. outputs], qualification));
        }
        return new(path, id, [.. builds], channels);
    }

    private static BuildQualification ParseQualification(object? node, string definition)
    {
        Dictionary<string, object?> map = Mapping(node, definition);
        switch (definition)
        {
            case "dotnet/nuget-package-v1":
                Only("probe", "declarations", "analyzers");
                return new NuGetQualification(Required("probe"), Optional("declarations"),
                    Strings("analyzers"));
            case "node/npm-package-v1":
                Only("probe", "test-before-build");
                return new NpmQualification(Required("probe"), Flag("test-before-build"));
            case "python/distribution-set-v1":
                Only("api", "tests", "static-version", "runtime-closure", "dependencies");
                return new PythonQualification(Strings("api"), Optional("tests") is { } tests
                    ? RelativePath(tests, allowDirectoryRoot: false) : null, Flag("static-version"),
                    Flag("runtime-closure"), Strings("dependencies"));
            case "ruby/gem-v1":
                Only("probe", "require", "nbgv-env", "locked-bundle", "rspec", "dependencies");
                return new RubyQualification(
                    Required("probe"), Optional("require"), Flag("nbgv-env"),
                    Flag("locked-bundle"), Flag("rspec"), Strings("dependencies"));
            case "dotnet/github-release-assets-v1":
                Only("asset-prefix", "executable", "editor");
                return new WindowsAssetsQualification(
                    Required("asset-prefix"), Required("executable"),
                    Required("editor"));
            case "node/browser-extension-v1":
                Only("manifest-name");
                return new BrowserExtensionQualification(Required("manifest-name"));
            default:
                return new InventoryQualification(
                    map.Keys.ToDictionary(key => key, key => key switch
                {
                    "analyzers" or "api" or "dependencies" => Strings(key),
                    "test-before-build" or "static-version" or "runtime-closure" or "nbgv-env" or
                        "locked-bundle" or "rspec" => [Flag(key) ? "true" : "false"],
                    "tests" => [RelativePath(Required(key), allowDirectoryRoot: false)],
                    "probe" or "declarations" or "require" or "asset-prefix" or "executable" or
                        "editor" or "manifest-name" => [Required(key)],
                    _ => throw new InvalidDataException(
                        "Unknown inventory qualification field: " + key),
                }, StringComparer.Ordinal));
        }

        void Only(params string[] allowed)
        {
            if (map.Keys.Except(allowed, StringComparer.Ordinal).Any())
                throw new InvalidDataException($"Invalid qualification fields for {definition}.");
        }
        string? Optional(string key) =>
            map.TryGetValue(key, out object? value) ? Text(value) : null;
        string Required(string key) => Optional(key) ??
            throw new InvalidDataException($"Missing {key} for {definition}.");
        bool Flag(string key) =>
            map.TryGetValue(key, out object? value) && Boolean(value, definition);
        string[] Strings(string key) => map.TryGetValue(key, out object? value)
            ? [.. Sequence(value).Select(Text)] : [];
    }

    private static Dictionary<string, object?> MappingWithOptional(object? node, string context,
        string[] required, string[] allowed)
    {
        Dictionary<string, object?> map = Mapping(node, context);
        if (required.Any(key => !map.ContainsKey(key)) ||
            map.Keys.Any(key => !allowed.Contains(key, StringComparer.Ordinal)))
            throw new InvalidDataException($"Invalid {context} declaration fields.");
        return map;
    }

    private static bool Boolean(object? node, string context) => Text(node) switch
    {
        "true" => true,
        "false" => false,
        _ => throw new InvalidDataException($"Expected boolean in {context}."),
    };

    private static string Identity(object? node)
    {
        string value = Text(node);
        if (value[0] == '-' || value[^1] == '-' ||
            value.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-')))
            throw new InvalidDataException("Expected a canonical release-unit identity.");
        return value;
    }
}
