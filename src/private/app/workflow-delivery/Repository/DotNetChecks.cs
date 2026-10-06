using System.Text.Json;
using System.Xml;
using Microsoft.Build.Utilities;
using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

// This is the finite native-retained contract, not an adopted package-quality recipe.
internal static class DotNetChecks
{
    internal const string Build = "dotnet/project-build-v1";
    internal const string Test = "dotnet/project-test-v1";

    internal static CheckSpec[] Expand(string project, DotNetGraphNode node)
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
                TransferJson.Default.MsBuildDimension),
            ["test-capability"] = node.TestCapability,
        };
        var build = new CheckSpec(new(project, Build, variant), dimensions, "windows-latest",
            true, []);
        return node.TestCapability == "None" ? [build] :
            [build, new(new(project, Test, variant), new(dimensions, StringComparer.Ordinal),
                "windows-latest", true, [build.Key])];
    }

    internal static DotNetGraphNode Read(CheckSpec check, string checkout)
    {
        if (check.Dimensions.Count != 3 ||
            !check.Dimensions.TryGetValue("globals", out string? originalGlobals) ||
            !check.Dimensions.TryGetValue("msbuild", out string? nativeDimension) ||
            !check.Dimensions.TryGetValue("test-capability", out string? capability))
            throw new InvalidDataException("Missing complete native check dimensions.");
        Dictionary<string, string> globals = JsonSerializer.Deserialize(
            originalGlobals, TransferJson.Default.DictionaryStringString)
            ?? throw new InvalidDataException("Missing original native globals.");
        MsBuildDimension dimension = JsonSerializer.Deserialize(nativeDimension,
            TransferJson.Default.MsBuildDimension)
            ?? throw new InvalidDataException("Missing native MSBuild dimension.");
        var node = new DotNetGraphNode(new(Path.GetFullPath(Path.Combine(checkout,
                check.Key.Target)), globals), false, dimension, "", "", [], capability);
        CheckSpec[] expected = Expand(check.Key.Target, node);
        CheckSpec? contract = expected.SingleOrDefault(item => item.Key == check.Key);
        if (contract is null || check.Runner != contract.Runner ||
            check.Required != contract.Required || check.Package is not null ||
            !check.Prerequisites.SequenceEqual(contract.Prerequisites))
            throw new InvalidDataException("Different retained native check contract.");
        return node;
    }

    internal static string[] Properties(Dictionary<string, string> globals)
    {
        var arguments = new List<string>();
        foreach ((string name, string value) in globals.OrderBy(pair => pair.Key,
                     StringComparer.Ordinal))
        {
            try { XmlConvert.VerifyNCName(name); }
            catch (XmlException exception)
            {
                throw new InvalidDataException("Unsupported native global name: " + name,
                    exception);
            }
            if (value is null || value.Contains('\0') ||
                (name.Equals("RestoreSources", StringComparison.OrdinalIgnoreCase) &&
                    (value.Contains(';') || value.Contains("://", StringComparison.Ordinal))))
                throw new InvalidDataException("Unsupported native global transport: " + name);
            // GlobalProperties already contains native escaped strings. Quote only
            // the value using the selected SDK; never escape those strings again.
            var builder = new CommandLineBuilder();
            builder.AppendSwitchIfNotNull("-property:" + name + "=", value);
            arguments.Add(builder.ToString());
        }
        return arguments.ToArray();
    }
}
