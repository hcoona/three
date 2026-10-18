using Microsoft.Build.Execution;
using Microsoft.Build.Graph;
using NuGet.Versioning;

namespace WorkflowDelivery.Repository;

// A finite projection of NBGV's native tool dependency, not a helper-node framework.
internal static class DotNetNbgvCaching
{
    private const string Package = "Nerdbank.GitVersioning";
    private static readonly StringComparer Paths = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static HashSet<ProjectGraphNode> Helpers(ProjectGraph graph) =>
        graph.ProjectNodes.Where(helper => helper.ReferencingProjects.Any(consumer =>
            References(consumer, helper).Any(reference =>
                reference.GetMetadataValue("NBGV_InnerProject") == "true"))).ToHashSet();

    internal static void Validate(ProjectGraphNode helper, ProjectGraphNode consumer,
        DotNetRestoreInputs[] restore)
    {
        ProjectItemInstance[] references = References(consumer, helper);
        if (helper.ProjectReferences.Count != 0 || references.Length == 0 ||
            references.Any(reference => reference.GetMetadataValue("NBGV_InnerProject") !=
                "true") || consumer.ProjectInstance.GetPropertyValue("NBGV_CacheMode") !=
                "MSBuildTargetCaching")
            throw new InvalidDataException("Unsupported native NBGV helper relation.");
        string path = Path.GetFullPath(helper.ProjectInstance.FullPath);
        DotNetInputProvider[] suppliers = restore.SelectMany(value => value.Packages)
            .Where(package => package.Identity.Equals(Package, StringComparison.OrdinalIgnoreCase)
                && Paths.Equals(path, Path.GetFullPath(
                    Path.Combine(package.Root, "build", "PrivateP2PCaching.proj"))))
            .ToArray();
        if (suppliers.Length == 0)
            throw new InvalidDataException(
                "Native NBGV helper is not supplied by the consumer's restored package.");
        string declared = Declared(consumer.ProjectInstance);
        // The consumer's own evaluated declaration is the pin; the restore must supply it.
        if (!VersionRange.TryParse(declared, out VersionRange? range) ||
            range.MinVersion is null || !range.IsMinInclusive ||
            suppliers.Any(package => !NuGetVersion.TryParse(package.Version,
                out NuGetVersion? restored) || restored != range.MinVersion))
            throw new InvalidDataException(
                "Native NBGV helper package differs from the consumer's declared pin: declared " +
                declared + ", restored " +
                string.Join(", ", suppliers.Select(package => package.Version).Distinct()) + ".");
    }

    // NuGet's central management projects GlobalPackageReference into PackageVersion items;
    // an explicit PackageReference version or VersionOverride takes precedence.
    private static string Declared(ProjectInstance consumer)
    {
        string[] Values(string type, string metadata) => consumer.GetItems(type)
            .Where(item => item.EvaluatedInclude.Equals(Package,
                StringComparison.OrdinalIgnoreCase))
            .Select(item => item.GetMetadataValue(metadata))
            .Where(value => value.Length != 0).Distinct(StringComparer.Ordinal).ToArray();
        string[] declared = Values("PackageReference", "VersionOverride");
        if (declared.Length == 0) declared = Values("PackageReference", "Version");
        if (declared.Length == 0) declared = Values("PackageVersion", "Version");
        return declared.Length == 1 ? declared[0] : throw new InvalidDataException(
            "Native NBGV helper consumer declares " + (declared.Length == 0 ? "no" :
                "ambiguous (" + string.Join(", ", declared) + ")") +
            " Nerdbank.GitVersioning version.");
    }

    private static ProjectItemInstance[] References(ProjectGraphNode consumer,
        ProjectGraphNode helper) => consumer.ProjectInstance.GetItems("ProjectReference")
        .Where(reference => Paths.Equals(
            Path.GetFullPath(reference.GetMetadataValue("FullPath")),
            Path.GetFullPath(helper.ProjectInstance.FullPath))).ToArray();
}
