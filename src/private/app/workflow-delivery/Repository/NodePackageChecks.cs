using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

internal static class NodePackageChecks
{
    internal static CheckSpec[] Expand(ReleaseUnitDeclaration unit,
        IReadOnlyDictionary<string, PnpmProject> projects,
        IReadOnlyDictionary<string, string> versions)
    {
        var checks = new List<CheckSpec>();
        string parent = Parent(unit.SourcePath);
        foreach (ReleaseBuild build in unit.Builds)
        {
            string entry = parent.Length == 0 ? build.EntryPoint : parent + "/" + build.EntryPoint;
            string directory = Parent(entry);
            if (build.Definition != "node/npm-package-v1" ||
                !entry.EndsWith("/package.json", StringComparison.Ordinal) ||
                !projects.TryGetValue(directory, out PnpmProject? project))
                throw new InvalidDataException($"Unresolved Node build {unit.Id}/{build.Id}.");
            if (project.PublishDirectory is not null)
                throw new InvalidDataException("The current Node package contract has no alternate "
                    + "publish-directory build.");
            if (build.Outputs is not [{ Role: "primary-package", Kind: "npm-tarball" }])
                throw new InvalidDataException(
                    $"Unsupported complete Node output set for {unit.Id}/{build.Id}.");
            if (!versions.TryGetValue(directory, out string? version) ||
                string.IsNullOrWhiteSpace(version))
                throw new InvalidDataException($"Missing native npm version for {directory}.");
            var subject = new PackageTarget(unit.Id, unit.SourcePath, build.Id, build.Definition,
                directory, entry, project.PublishDirectory, version,
                build.Outputs.Select(output => new PackageOutput(output.Id, output.Role,
                    output.Kind)).ToArray());
            string target = "release/" + unit.Id + "/" + build.Id;
            var pack = new CheckKey(target, "node/npm-artifact-v1", "default");
            checks.Add(new(pack, [], "ubuntu-latest", true,
                [new(directory, "node/project-build-v1", "default")], subject));
            checks.Add(new(new(target, "node/npm-artifact-contents-v1", "default"), [],
                "ubuntu-latest", true, [pack], subject));
            checks.Add(new(new(target, "node/npm-install-import-v1", "default"), [],
                "ubuntu-latest", true, [pack], subject));
        }
        return [.. checks];
    }

    internal static string Parent(string path)
    {
        int separator = path.LastIndexOf('/');
        return separator < 0 ? "" : path[..separator];
    }
}
