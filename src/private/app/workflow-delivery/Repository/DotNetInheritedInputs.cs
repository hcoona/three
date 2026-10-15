namespace WorkflowDelivery.Repository;

// The one application table of repository-level .NET inputs and inherited MSBuild/NuGet/Git
// configuration names. Native evaluation remains authoritative for actual project consumers.
internal static class DotNetInheritedInputs
{
    internal static readonly string[] Root = [".editorconfig", ".gitattributes", "global.json",
        "NuGet.Config", "nuget.config", ".config/dotnet-tools.json", "version.json",
        "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props",
        "dirs.proj"];

    // A root-file or inherited-name path consumed by projects under any supplied directory.
    internal static bool Consumes(string path, IEnumerable<string> roots)
    {
        if (Root.Contains(path, StringComparer.Ordinal)) return true;
        int separator = path.LastIndexOf('/');
        string name = path[(separator + 1)..];
        string directory = separator < 0 ? "" : path[..separator];
        bool inherited = name is ".editorconfig" or ".gitattributes" or "Directory.Build.props" or
            "Directory.Build.targets" or "Directory.Packages.props" or "version.json" or
            "NuGet.Config" or "nuget.config" or "global.json";
        return roots.Any(root => Under(path, root) ||
            (inherited && (directory.Length == 0 || Under(root, directory))));
    }

    private static bool Under(string path, string directory) => path == directory ||
        path.StartsWith(directory + "/", StringComparison.Ordinal);
}
