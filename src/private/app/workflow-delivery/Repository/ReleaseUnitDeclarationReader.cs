using static WorkflowDelivery.Repository.StrictYaml;

namespace WorkflowDelivery.Repository;

internal sealed record ReleaseOutput(string Id, string Role, string Kind);

internal sealed record ReleaseQualificationSpec(
    string? Probe = null,
    string? Declarations = null,
    string[]? Analyzers = null,
    string[]? Api = null,
    string? Tests = null,
    bool TestBeforeBuild = false,
    bool StaticVersion = false,
    bool RuntimeClosure = false,
    string? Require = null,
    bool NbgvEnv = false,
    bool LockedBundle = false,
    bool RSpec = false,
    string[]? Dependencies = null,
    string? AssetPrefix = null,
    string? Executable = null,
    string? Editor = null,
    string? ManifestName = null);

internal sealed record ReleaseBuild(
    string Id,
    string Definition,
    string EntryPoint,
    ReleaseOutput[] Outputs,
    ReleaseQualificationSpec? Qualification = null);

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

    private static readonly string[] QualificationAllowedKeys =
    [
        "probe", "declarations", "analyzers", "api", "tests", "test-before-build",
        "static-version", "runtime-closure", "require", "nbgv-env", "locked-bundle",
        "rspec", "dependencies", "asset-prefix", "executable", "editor", "manifest-name",
    ];

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
            ReleaseQualificationSpec? qualification = build.TryGetValue("qualification",
                out object? qualNode) ? ParseQualification(qualNode, declaration) : null;
            builds.Add(new(buildId, definition, entryPoint, [.. outputs], qualification));
        }
        return new(path, id, [.. builds], channels);
    }

    private static ReleaseQualificationSpec ParseQualification(object? node, string context)
    {
        Dictionary<string, object?> map = MappingWithOptional(node, context, [],
            QualificationAllowedKeys);
        string? OptionalText(string key) => map.TryGetValue(key, out object? v) ? Text(v) : null;
        bool Flag(string key) => map.TryGetValue(key, out object? v) && Boolean(v, context);
        string[]? Strings(string key) => map.TryGetValue(key, out object? v)
            ? [.. Sequence(v).Select(Text)] : null;
        return new(
            Probe: OptionalText("probe"),
            Declarations: OptionalText("declarations"),
            Analyzers: Strings("analyzers"),
            Api: Strings("api"),
            Tests: OptionalText("tests") is { } t
                ? RelativePath(t, allowDirectoryRoot: false) : null,
            TestBeforeBuild: Flag("test-before-build"),
            StaticVersion: Flag("static-version"),
            RuntimeClosure: Flag("runtime-closure"),
            Require: OptionalText("require"),
            NbgvEnv: Flag("nbgv-env"),
            LockedBundle: Flag("locked-bundle"),
            RSpec: Flag("rspec"),
            Dependencies: Strings("dependencies"),
            AssetPrefix: OptionalText("asset-prefix"),
            Executable: OptionalText("executable"),
            Editor: OptionalText("editor"),
            ManifestName: OptionalText("manifest-name"));
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
