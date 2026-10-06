using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;

namespace WorkflowDelivery.Repository;

// These associations classify observed inputs in this invocation, not arbitrary
// provider residence or a persistent provenance lifecycle.
internal sealed class DotNetNativeInputs
{
    private static readonly StringComparer Paths = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private readonly HashSet<string> committed;
    private readonly DotNetInputProvider[] providers;
    private readonly Dictionary<string, DotNetInputProvider> generated;
    private readonly Dictionary<string, DotNetInputProvider> artifacts = new(Paths);

    internal DotNetNativeInputs(ProjectInstance project, ResolvedImport[] imports,
        DotNetRestoreInputs[] restore, string[] committedPaths)
    {
        committed = new(committedPaths.Select(Path.GetFullPath), Paths);
        var native = new List<DotNetInputProvider>();
        foreach (string name in new[] { "MSBuildToolsPath", "MSBuildBinPath" })
        {
            string path = project.GetPropertyValue(name);
            if (Path.IsPathFullyQualified(path))
                native.Add(new("Toolset", Path.GetFullPath(path), name, ""));
        }
        foreach (ResolvedImport import in imports)
        {
            if (import.SdkResult is not { Success: true } sdk) continue;
            foreach (string root in new[] { sdk.Path }.Concat(sdk.AdditionalPaths ?? []))
            {
                if (!Path.IsPathFullyQualified(root))
                    throw new InvalidDataException("Native resolved SDK path is unavailable.");
                var provider = new DotNetInputProvider("Sdk", Path.GetFullPath(root),
                    sdk.SdkReference.Name, sdk.Version ?? "");
                native.Add(provider);
                if (provider.Identity != "Microsoft.Build.Artifacts") continue;
                string sdkProps = Path.Combine(provider.Root, "Sdk.props");
                string sdkTargets = Path.Combine(provider.Root, "Sdk.targets");
                string build = Path.GetFullPath(Path.Combine(provider.Root, "..", "build"));
                AddArtifact("Microsoft.Build.Artifacts.props", sdkProps);
                AddArtifact("Microsoft.Build.Artifacts.targets", sdkTargets);
                AddArtifact("Microsoft.Build.Artifacts.Common.props",
                    Path.Combine(build, "Microsoft.Build.Artifacts.props"));
                AddArtifact("Microsoft.Build.Artifacts.Common.targets",
                    Path.Combine(build, "Microsoft.Build.Artifacts.targets"));

                void AddArtifact(string name, string importer)
                {
                    string path = Path.Combine(build, name);
                    if (imports.Any(value => Paths.Equals(value.ImportedProject.FullPath, path)
                        && Paths.Equals(value.ImportingElement?.ContainingProject.FullPath,
                            importer)))
                        artifacts[path] = provider;
                }
            }
        }
        native.AddRange(restore.SelectMany(value => value.Packages));
        providers = [.. native];
        generated = new(Paths);
        foreach (string path in restore.SelectMany(value => value.GeneratedImports))
            generated.TryAdd(path, new("RestoreGenerated", path, project.FullPath, ""));
    }

    internal DotNetGraphInput Classify(DotNetGraphInput input)
    {
        if (committed.Contains(input.Path)) return input with { Provider = null };
        if (input.Provider is not null || !File.Exists(input.Path) ||
            input.Stage != "Evaluation" || input.Role is "Project" or
                "CentralPackageConfiguration" || input.Role.EndsWith("Candidate",
                    StringComparison.Ordinal))
            return input;
        if (input.Role == "Import" && generated.TryGetValue(input.Path,
                out DotNetInputProvider? generatedProvider))
            return input with { Provider = generatedProvider };
        if (input.Role == "Import" && artifacts.TryGetValue(input.Path,
                out DotNetInputProvider? artifactsProvider))
            return input with { Provider = artifactsProvider };
        DotNetInputProvider? provider = providers.FirstOrDefault(value =>
            input.Path.StartsWith(Path.TrimEndingDirectorySeparator(value.Root) +
                Path.DirectorySeparatorChar, OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
        return input with { Provider = provider };
    }
}
