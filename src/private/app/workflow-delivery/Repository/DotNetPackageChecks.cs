using System.Text.Json;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record DotNetPackageContext(string Project, ReleaseUnitDeclaration Unit,
    ReleaseBuild Build, DotNetGraphNode Node);

// One registered product contract; native metadata remains authoritative.
internal static class DotNetPackageChecks
{
    internal const string Preset = "dotnet/hcoona-release-smoke-github-packages-v1";
    internal const string Definition = "dotnet/nuget-package-v1";
    internal const string Pack = "dotnet/nuget-artifact-v1";
    internal const string Contents = "dotnet/nuget-artifact-contents-v1";
    internal const string Consumer = "dotnet/nuget-restore-build-invoke-v1";
    internal static readonly string[] NativeProperties = DotNetPackageResults.NativeProperties;

    internal static DotNetPackageContext Resolve(ProjectFacts project, DotNetSelection selection)
    {
        if (!selection.CandidateNodes.TryGetValue(project.Id, out DotNetGraphNode[]? nodes))
            throw new InvalidDataException("Missing selected native package identities.");
        return Resolve(project, nodes, selection.CandidateUnits);
    }

    private static DotNetPackageContext Resolve(ProjectFacts project, DotNetGraphNode[] nodes,
        ReleaseUnitDeclaration[] units)
    {
        if (project.Origin != CheckOrigin.Preset || project.QualityPreset != Preset ||
            project.ReleaseUnit is null)
            throw new InvalidDataException("Unresolved selected .NET package quality contract.");
        DotNetGraphNode[] inner = nodes.Where(node => !node.OuterBuild).ToArray();
        if (inner is not [DotNetGraphNode node] || node.Dimension.Configuration != "Debug" ||
            node.Dimension.TargetFramework != "net10.0" || node.Dimension.RuntimeIdentifier != "" ||
            node.TestCapability != "None")
            throw new InvalidDataException("Unresolved complete native package variant.");
        DotNetRepositoryReader.Key(node.Identity);
        DotNetChecks.Properties(node.Identity.Globals);
        if (node.Identity.Globals.Keys.Any(name =>
                name.Equals("PackageOutputPath", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Native package-output storage conflicts with globals.");
        ReleaseUnitDeclaration unit = units.SingleOrDefault(unit =>
            unit.Id == project.ReleaseUnit) ??
            throw new InvalidDataException("Missing selected release-unit declaration.");
        if (unit.Builds is not [ReleaseBuild build] ||
            DotNetReleaseUnits.Entry(unit, build) != project.Id || build.Definition != Definition ||
            build.Outputs is not [{ Role: "primary-package", Kind: "nuget-package" }])
            throw new InvalidDataException("Unresolved complete native package/output definition.");
        return new(project.Id, unit, build, node);
    }

    internal static async Task<CheckSpec[]> ProjectAsync(DotNetPackageContext context,
        string checkout, string response, int deadline,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        NativeCommand command = Query(context.Node, checkout, response, "GetBuildVersion",
            "GetBuildVersion", deadline);
        NativeCommandResult result = await execute(command, token);
        token.ThrowIfCancellationRequested();
        if (!result.Succeeded)
            throw new InvalidDataException("Native NBGV projection failed: " +
                result.Termination + ", exit " + result.ExitCode + ". " + result.Error);
        using JsonDocument document = await DotNetPackageResults.ResponseAsync(response, token);
        DotNetPackageResults.Target(document.RootElement, "GetBuildVersion");
        Dictionary<string, string> values = DotNetPackageResults.Properties(document.RootElement);
        Validate(context.Node, values);
        return Expand(context, values);
    }

    internal static NativeCommand Query(DotNetGraphNode node, string checkout,
        string response, string targets, string returned, int deadline,
        string[]? storage = null) => new("dotnet", checkout,
        ["msbuild", node.Identity.Project, "-nologo", "-noAutoResponse", "-target:" + targets,
            "-getTargetResult:" + returned, "-getProperty:" + string.Join(',', NativeProperties),
            "-getResultOutputFile:" + response, .. DotNetChecks.Properties(node.Identity.Globals),
            .. (storage ?? [])], deadline);

    internal static CheckSpec[] Expand(DotNetPackageContext context,
        Dictionary<string, string> values)
    {
        Validate(context.Node, values);
        var expectations = new Dictionary<string, string>(values, StringComparer.Ordinal);
        // Native physical identity belongs to each checkout, not to the transferred plan.
        expectations.Remove("MSBuildProjectFullPath");
        return PortableChecks(context, expectations);
    }

    private static CheckSpec[] PortableChecks(DotNetPackageContext context,
        Dictionary<string, string> values)
    {
        ValidateExpectations(context.Node, values);
        ReleaseBuild build = context.Build;
        var subject = new PackageTarget(context.Unit.Id, context.Unit.SourcePath, build.Id,
            build.Definition, DotNetRepositoryReader.Parent(context.Project), context.Project,
            null, values["NuGetPackageVersion"], build.Outputs.Select(output =>
                new PackageOutput(output.Id, output.Role, output.Kind)).ToArray());
        string target = "release/" + subject.Unit + "/" + subject.Build;
        string variant = DotNetRepositoryReader.Key(context.Node.Identity).Globals;
        var dimensions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["globals"] = JsonSerializer.Serialize(context.Node.Identity.Globals,
                TransferJson.Default.DictionaryStringString),
            ["msbuild"] = JsonSerializer.Serialize(context.Node.Dimension,
                TransferJson.Default.MsBuildDimension),
            ["native"] = JsonSerializer.Serialize(
                values, TransferJson.Default.DictionaryStringString),
        };
        var pack = new CheckKey(target, Pack, variant);
        var contents = new CheckKey(target, Contents, variant);
        return [new(pack, dimensions, "windows-latest", true, [], subject),
            new(contents, new(dimensions, StringComparer.Ordinal), "windows-latest", true,
                [pack], subject),
            new(new(target, Consumer, variant), new(dimensions, StringComparer.Ordinal),
                "windows-latest", true, [pack, contents], subject)];
    }

    internal static (DotNetGraphNode Node, Dictionary<string, string> Values) Read(
        CheckSpec check, string checkout, ReleaseUnitDeclaration unit)
    {
        PackageTarget subject = check.Package ??
            throw new InvalidDataException("Missing native package subject.");
        ReleaseBuild build = unit.Builds.SingleOrDefault(build => build.Id == subject.Build) ??
            throw new InvalidDataException("Missing declared native package build.");
        if (check.Dimensions.Count != 3 ||
            !check.Dimensions.TryGetValue("globals", out string? globals) ||
            !check.Dimensions.TryGetValue("msbuild", out string? dimension) ||
            !check.Dimensions.TryGetValue("native", out string? native))
            throw new InvalidDataException("Missing full native package dimensions.");
        var node = new DotNetGraphNode(new(
            Path.GetFullPath(Path.Combine(checkout, subject.EntryPoint)),
            JsonSerializer.Deserialize(globals, TransferJson.Default.DictionaryStringString) ??
                throw new InvalidDataException("Missing native package globals.")), false,
            JsonSerializer.Deserialize(dimension, TransferJson.Default.MsBuildDimension) ??
                throw new InvalidDataException("Missing native package dimension."), "", "", [],
            "None");
        Dictionary<string, string> values = JsonSerializer.Deserialize(native,
            TransferJson.Default.DictionaryStringString) ??
            throw new InvalidDataException("Missing native package expectations.");
        DotNetPackageContext context = Resolve(new(subject.EntryPoint, subject.Directory,
            [], [], subject.Unit, Preset, []), [node], [unit]);
        CheckSpec? expected =
            PortableChecks(context, values).SingleOrDefault(item => item.Key == check.Key);
        if (expected is null || expected.Runner != check.Runner || !check.Required ||
            !expected.Prerequisites.SequenceEqual(check.Prerequisites) ||
            !ImpactPlanner.SamePackage(expected.Package, subject))
            throw new InvalidDataException("Different complete native package contract.");
        return (node, values);
    }

    internal static void Validate(DotNetGraphNode node, Dictionary<string, string> values) =>
        DotNetPackageResults.Validate(node.Identity.Project, node.Dimension.TargetFramework,
            node.Dimension.RuntimeIdentifier, values);

    private static void ValidateExpectations(DotNetGraphNode node,
        Dictionary<string, string> values) => DotNetPackageResults.ValidateExpectations(
            node.Dimension.TargetFramework, node.Dimension.RuntimeIdentifier, values);
}
