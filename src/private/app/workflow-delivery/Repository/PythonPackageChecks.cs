using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

internal static class PythonPackageChecks
{
    internal const string Preset = "python/hcoona-release-smoke-python-v1";
    internal const string Build = "python/distribution-set-v1";
    internal const string Contents = "python/distribution-contents-v1";
    internal const string Wheel = "python/wheel-install-import-v1";
    internal const string Sdist = "python/sdist-build-install-import-v1";
    internal static readonly string[] Kinds = [Build, Contents, Wheel, Sdist];

    internal static CheckSpec[] Expand(ReleaseUnitDeclaration unit,
        PythonMetadataMember[] members, IReadOnlyDictionary<string, string> versions)
    {
        var checks = new List<CheckSpec>();
        string parent = NodePackageChecks.Parent(unit.SourcePath);
        foreach (ReleaseBuild build in unit.Builds)
        {
            string entry = parent.Length == 0 ? build.EntryPoint : parent + "/" + build.EntryPoint;
            string directory = NodePackageChecks.Parent(entry);
            if (directory.Length == 0) directory = ".";
            if (build.Definition != Build || entry != (directory == "." ? "pyproject.toml" :
                    directory + "/pyproject.toml") ||
                members.Count(member => member.Directory == directory) != 1 ||
                !versions.TryGetValue(directory, out string? version) ||
                string.IsNullOrWhiteSpace(version))
                throw new InvalidDataException("Unresolved native Python package subject.");
            if (build.Outputs.Length != 2 ||
                build.Outputs.Count(output => output is
                    { Role: "primary-package", Kind: "python-wheel" }) != 1 ||
                build.Outputs.Count(output => output is
                    { Role: "source-package", Kind: "python-sdist" }) != 1)
                throw new InvalidDataException("Unsupported complete Python distribution set.");
            var subject = new PackageTarget(unit.Id, unit.SourcePath, build.Id, build.Definition,
                directory, entry, null, version, build.Outputs.Select(output =>
                    new PackageOutput(output.Id, output.Role, output.Kind)).ToArray());
            string target = "release/" + unit.Id + "/" + build.Id;
            var pack = new CheckKey(target, Build, "default");
            var contents = new CheckKey(target, Contents, "default");
            checks.Add(new(pack, [], "ubuntu-latest", true, [], subject));
            checks.Add(new(contents, [], "ubuntu-latest", true, [pack], subject));
            checks.Add(new(new(target, Wheel, "default"), [], "ubuntu-latest", true,
                [pack, contents], subject));
            checks.Add(new(new(target, Sdist, "default"), [], "ubuntu-latest", true,
                [pack, contents], subject));
        }
        return [.. checks];
    }
}
