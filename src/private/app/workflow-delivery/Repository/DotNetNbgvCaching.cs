using Microsoft.Build.Execution;
using Microsoft.Build.Graph;

namespace WorkflowDelivery.Repository;

// A finite projection of NBGV's native tool dependency, not a helper-node framework.
internal static class DotNetNbgvCaching
{
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
        if (!restore.SelectMany(value => value.Packages).Any(package =>
            package.Identity.Equals("Nerdbank.GitVersioning", StringComparison.OrdinalIgnoreCase)
            && package.Version == "3.10.94" && Paths.Equals(path,
                Path.GetFullPath(Path.Combine(package.Root, "build", "PrivateP2PCaching.proj")))))
            throw new InvalidDataException(
                "Native NBGV helper is not supplied by the consumer's pinned restored package.");
    }

    private static ProjectItemInstance[] References(ProjectGraphNode consumer,
        ProjectGraphNode helper) => consumer.ProjectInstance.GetItems("ProjectReference")
        .Where(reference => Paths.Equals(
            Path.GetFullPath(reference.GetMetadataValue("FullPath")),
            Path.GetFullPath(helper.ProjectInstance.FullPath))).ToArray();
}
