namespace WorkflowDelivery.Repository;

// Join application declarations to exact native entries before impact closure.
internal static class DotNetReleaseUnits
{
    internal static string Entry(ReleaseUnitDeclaration unit, ReleaseBuild build)
    {
        string parent = DotNetRepositoryReader.Parent(unit.SourcePath);
        string entry = parent == "." ? build.EntryPoint : parent + "/" + build.EntryPoint;
        CI.ImpactPlanner.ValidatePath(entry);
        return entry;
    }

    internal static Dictionary<string, string> Associate(DotNetRevisionInputs inputs)
    {
        var projects = inputs.Nodes.Where(node => node.Project != "dirs.proj")
            .Select(node => node.Project).ToHashSet(StringComparer.Ordinal);
        var units = new HashSet<string>(StringComparer.Ordinal);
        var membership = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ReleaseUnitDeclaration unit in inputs.Units)
        {
            if (!units.Add(unit.Id))
                throw new InvalidDataException("Ambiguous release-unit identity.");
            RequireFile(unit.SourcePath);
            foreach (ReleaseBuild build in unit.Builds)
            {
                string entry = Entry(unit, build);
                if (!projects.Contains(entry))
                {
                    if (build.Definition.StartsWith("dotnet/", StringComparison.Ordinal))
                        throw new InvalidDataException("Unresolved .NET release-unit entry: " +
                            entry);
                    continue;
                }
                RequireFile(entry);
                if (membership.TryGetValue(entry, out string? previous) && previous != unit.Id)
                    throw new InvalidDataException("A native project belongs to multiple units.");
                membership[entry] = unit.Id;
            }
        }
        return membership;

        void RequireFile(string path)
        {
            if (!inputs.Revision.Entries.Any(entry => entry.Path == path &&
                    entry.ObjectType == "blob" && entry.Mode is "100644" or "100755"))
                throw new InvalidDataException("Release-unit input is not committed source: " +
                    path);
        }
    }
}
