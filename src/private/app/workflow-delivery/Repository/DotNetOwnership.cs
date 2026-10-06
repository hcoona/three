using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.Globbing;

namespace WorkflowDelivery.Repository;

// Git supplies committed entries; the selected MSBuild supplies region exclusions.
internal sealed class DotNetOwnership
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private readonly string[] _regions;
    private readonly string[] _paths;

    internal DotNetOwnership(string root, IEnumerable<ProjectInstance> projects,
        string[] committedPaths)
    {
        _regions = projects.Where(IsManagedProject).Select(project =>
                Path.GetDirectoryName(project.FullPath)!)
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase :
                StringComparer.Ordinal).OrderByDescending(path => path.Length).ToArray();
        _paths = committedPaths.Select(path =>
        {
            if (!Path.IsPathFullyQualified(path))
                throw new InvalidDataException(
                    "Committed ownership coordinates must be absolute.");
            string full = Path.GetFullPath(path);
            if (!Inside(root, full))
                throw new InvalidDataException(
                    "Committed ownership entry is outside its endpoint.");
            return full;
        }).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    internal string[] Read(ProjectInstance project)
    {
        if (!IsManagedProject(project)) return [];
        string directory = Path.GetDirectoryName(project.FullPath)!;
        MSBuildGlob[] excludes = Excludes("DefaultItemExcludes")
            .Concat(Excludes("DefaultExcludesInProjectFolder")).ToArray();
        return _paths.Where(path =>
            string.Equals(_regions.FirstOrDefault(region => Inside(region, path)), directory,
                PathComparison) && !excludes.Any(glob => glob.IsMatch(path))).ToArray();

        IEnumerable<MSBuildGlob> Excludes(string name)
        {
            ProjectPropertyInstance? property = project.GetProperty(name);
            if (property is null) yield break;
            // Split while escaped so a literal semicolon cannot become a list separator.
            foreach (string spec in ProjectInstance.GetPropertyValueEscaped(property)
                .Split(';', StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries))
            {
                if (spec.Contains("%2a", StringComparison.OrdinalIgnoreCase) ||
                    spec.Contains("%3f", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        "Unsupported escaped native ownership wildcard.");
                MSBuildGlob glob = MSBuildGlob.Parse(directory, ProjectCollection.Unescape(spec));
                if (!glob.IsLegal)
                    throw new InvalidDataException("Illegal native ownership filespec: " + name);
                yield return glob;
            }
        }
    }

    internal static bool IsManagedProject(ProjectInstance project)
    {
        string extension = Path.GetExtension(project.FullPath);
        return extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Inside(string directory, string path) => path.StartsWith(
        Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar, PathComparison);
}
