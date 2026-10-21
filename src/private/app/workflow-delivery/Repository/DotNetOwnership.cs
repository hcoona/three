using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
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
    // Request-local: graph nodes often share a directory and the same exclusion specs.
    private readonly ConcurrentDictionary<(string Directory, string Spec), MSBuildGlob> _globs =
        new();

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

    // Returns one deferred result per project, in order. Invoking a result returns its paths or
    // rethrows its failure, so callers keep the serial failure order.
    internal Func<string[]>[] Read(IReadOnlyList<ProjectInstance> projects)
    {
        // ProjectInstance documents no concurrent-read contract; read its inputs serially.
        (string? Directory, (string Name, string Spec)[] Specs)[] requests = projects.Select(
            project => IsManagedProject(project) ? (Path.GetDirectoryName(project.FullPath)!,
                [.. Specs(project, "DefaultItemExcludes"),
                    .. Specs(project, "DefaultExcludesInProjectFolder")]) :
                ((string?)null, Array.Empty<(string, string)>())).ToArray();
        var results = new Func<string[]>[requests.Length];
        // MSBuild parses and matches these globs concurrently during its own evaluation.
        Parallel.For(0, requests.Length, index =>
        {
            try
            {
                string[] owned = Read(requests[index].Directory, requests[index].Specs);
                results[index] = () => owned;
            }
            catch (Exception error)
            {
                var failure = ExceptionDispatchInfo.Capture(error);
                results[index] = () =>
                {
                    failure.Throw();
                    return [];
                };
            }
        });
        return results;

        static IEnumerable<(string, string)> Specs(ProjectInstance project, string name)
        {
            ProjectPropertyInstance? property = project.GetProperty(name);
            if (property is null) return [];
            // Split while escaped so a literal semicolon cannot become a list separator.
            return ProjectInstance.GetPropertyValueEscaped(property).Split(';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(spec => (name, spec));
        }
    }

    private string[] Read(string? directory, (string Name, string Spec)[] specs)
    {
        if (directory is null) return [];
        MSBuildGlob[] excludes = specs.Select(entry =>
        {
            if (entry.Spec.Contains("%2a", StringComparison.OrdinalIgnoreCase) ||
                entry.Spec.Contains("%3f", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unsupported escaped native ownership wildcard.");
            MSBuildGlob glob = _globs.GetOrAdd((directory, entry.Spec), key =>
                MSBuildGlob.Parse(key.Directory, ProjectCollection.Unescape(key.Spec)));
            if (!glob.IsLegal)
                throw new InvalidDataException("Illegal native ownership filespec: " +
                    entry.Name);
            return glob;
        }).ToArray();
        return _paths.Where(path =>
            string.Equals(_regions.FirstOrDefault(region => Inside(region, path)), directory,
                PathComparison) && !excludes.Any(glob => glob.IsMatch(path))).ToArray();
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
