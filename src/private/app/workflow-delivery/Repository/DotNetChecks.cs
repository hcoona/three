using System.Text.Json;
using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

// This is the finite native-retained contract, not an adopted package-quality recipe.
internal static class DotNetChecks
{
    internal const string Build = "dotnet/project-build-v1";
    internal const string Test = "dotnet/project-test-v1";

    internal static CheckSpec[] Expand(string project, DotNetGraphNode node,
        IReadOnlyDictionary<string, string>? versions = null)
    {
        ImpactPlanner.ValidatePath(project);
        if (!new[] { ".csproj", ".fsproj", ".vbproj" }.Contains(Path.GetExtension(project),
                StringComparer.OrdinalIgnoreCase) || node.OuterBuild ||
            node.Dimension.Configuration != "Debug" ||
            node.Dimension.TargetFramework is null || node.Dimension.RuntimeIdentifier is null ||
            node.TestCapability is not ("None" or "MTP" or "VSTest"))
            throw new InvalidDataException("Unsupported retained native check context.");
        string variant = DotNetRepositoryReader.Key(node.Identity).Globals;
        Properties(node.Identity.Globals);
        var dimensions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["globals"] = JsonSerializer.Serialize(node.Identity.Globals,
                TransferJson.Default.DictionaryStringString),
            ["msbuild"] = JsonSerializer.Serialize(node.Dimension,
                DotNetNativeJson.Default.MsBuildDimension),
            ["test-capability"] = node.TestCapability,
            [DotNetTools.Dimension] = DotNetTools.Serialize(DotNetTools.Planned(
                node.RequiredTools ?? [], versions ?? new Dictionary<string, string>())),
        };
        var build = new CheckSpec(new(project, Build, variant), dimensions, "windows-latest",
            true, []);
        return node.TestCapability == "None" ? [build] :
            [build, new(new(project, Test, variant), new(dimensions, StringComparer.Ordinal),
                "windows-latest", true, [build.Key])];
    }

    internal static DotNetGraphNode Read(CheckSpec check, string checkout)
    {
        if (check.Dimensions.Count != 4 ||
            !check.Dimensions.TryGetValue("globals", out string? originalGlobals) ||
            !check.Dimensions.TryGetValue("msbuild", out string? nativeDimension) ||
            !check.Dimensions.TryGetValue("test-capability", out string? capability) ||
            !check.Dimensions.TryGetValue(DotNetTools.Dimension, out string? toolDimension))
            throw new InvalidDataException("Missing complete native check dimensions.");
        Dictionary<string, string> tools = DotNetTools.Parse(toolDimension);
        Dictionary<string, string> globals = JsonSerializer.Deserialize(
            originalGlobals, TransferJson.Default.DictionaryStringString)
            ?? throw new InvalidDataException("Missing original native globals.");
        MsBuildDimension dimension = JsonSerializer.Deserialize(nativeDimension,
            DotNetNativeJson.Default.MsBuildDimension)
            ?? throw new InvalidDataException("Missing native MSBuild dimension.");
        var node = new DotNetGraphNode(new(Path.GetFullPath(Path.Combine(checkout,
                check.Key.Target)), globals), false, dimension, "", "", [], capability,
            RequiredTools: DotNetRequiredTools.Names(tools.Keys));
        if (tools.Count != 0 && capability == "None")
            throw new InvalidDataException("Only test projects declare required tools.");
        CheckSpec[] expected = Expand(check.Key.Target, node, tools);
        CheckSpec? contract = expected.SingleOrDefault(item => item.Key == check.Key);
        if (contract is null || check.Runner != contract.Runner ||
            check.Required != contract.Required || check.Package is not null ||
            !check.Prerequisites.SequenceEqual(contract.Prerequisites))
            throw new InvalidDataException("Different retained native check contract.");
        return node;
    }

    internal static string[] Properties(Dictionary<string, string> globals) =>
        DotNetTaskHost.Properties(globals);
}
