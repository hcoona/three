namespace WorkflowDelivery.Repository;

// Bind one endpoint graph to native operation answers; UV owns all activity semantics.
internal sealed class PythonPlanningActivities
{
    private readonly PythonMetadata metadata;
    private readonly PythonBuildAssociation[] builds;
    private readonly string[] groupOwners;
    private readonly Dictionary<(string Expression, string? Extra), string> markers = [];

    internal PythonSupplementRequest Request { get; }

    internal PythonPlanningActivities(PythonMetadata metadata, PythonBuildAssociation[] builds,
        string interpreter, string cache, PythonPassiveProject[] projects,
        PythonGroupOperation[] operations)
    {
        this.metadata = metadata;
        this.builds = builds;
        var contexts = metadata.Members.Select(member => member.Directory)
            .Append(".").ToHashSet(StringComparer.Ordinal);
        string[] directories = operations.Select(operation =>
            PythonMetadataReader.Relative(metadata.Root, operation.Directory)).ToArray();
        if (!contexts.SetEquals(directories))
            throw new InvalidDataException("Python activity operations omit native contexts.");
        groupOwners = operations.Select((operation, index) =>
        {
            if (operation.Packages.Length == 0) return directories[index];
            if (operation.Packages.Length != 1)
                throw new InvalidDataException("Unsupported Python group operation package scope.");
            PythonMetadataMember[] selected = metadata.Members.Where(member =>
                member.Name == operation.Packages[0]).ToArray();
            if (selected.Length != 1)
                throw new InvalidDataException(
                    "Python group operation has no unique native owner.");
            return selected[0].Directory;
        }).ToArray();

        var inputs = new List<PythonMarkerInput>();
        foreach (PythonMetadataNode node in metadata.Nodes.Values)
            foreach (PythonMetadataEdge edge in node.Dependencies)
                Add(edge.Marker, node.Kind == "extra" ? node.Dimension : null);
        foreach (PythonBuildAssociation build in builds) Add(build.Marker, null);
        Request = new(metadata.Root, interpreter, cache, projects, operations,
            inputs.ToArray(), [new("workspace-python", metadata.RequiresPython)]);

        void Add(string? expression, string? extra)
        {
            if (expression is null || markers.ContainsKey((expression, extra))) return;
            string id = "marker-" + inputs.Count.ToString(
                System.Globalization.CultureInfo.InvariantCulture);
            markers.Add((expression, extra), id);
            inputs.Add(new(id, expression, extra is null ? [] : [extra]));
        }
    }

    internal PythonProjectDependencies[] Project(PythonSupplementResult result)
    {
        PythonNativeActivities activities = PythonNativeActivities.Read(Request, result);
        // Directory/base contexts remain even when a singleton package owns the groups.
        PythonOperationGroups[] groups = Request.GroupOperations.Select(operation =>
            new PythonOperationGroups(PythonMetadataReader.Relative(metadata.Root,
                operation.Directory), [])).Concat(Request.GroupOperations.Select((operation,
                index) => new PythonOperationGroups(groupOwners[index],
                    activities.Groups[operation.Id]))).ToArray();
        return PythonDependencyProjection.Project(metadata, groups, builds, Active,
            activities.PythonConstraints["workspace-python"]);

        bool Active(string expression, string[] extras)
        {
            if (extras.Length > 1 || !markers.TryGetValue(
                (expression, extras.SingleOrDefault()), out string? id))
                throw new InvalidDataException("Python marker scope has no native request.");
            return activities.Markers[id];
        }
    }
}
