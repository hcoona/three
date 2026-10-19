using WorkflowDelivery.CI;
using WorkflowDelivery.Ecosystems;

namespace WorkflowDelivery.Repository;

// Expand the adopted gem recipe; RubyGems supplies the source package identity.
internal static class RubyPackageChecks
{
    internal const string Preset = "ruby/hcoona-release-smoke-ruby-v1";
    internal const string Build = "ruby/gem-v1";
    internal const string Contents = "ruby/gem-contents-v1";
    internal const string Consumer = "ruby/gem-install-require-v1";
    internal static readonly string[] Kinds = [Build, Contents, Consumer];

    internal static CheckSpec[] Expand(ReleaseUnitDeclaration unit, string root,
        IReadOnlyDictionary<string, RubySourceGemFacts> sources)
    {
        if (!Path.IsPathFullyQualified(root))
            throw new InvalidDataException("Ruby package expansion requires its native endpoint.");
        var checks = new List<CheckSpec>();
        string parent = NodePackageChecks.Parent(unit.SourcePath);
        foreach (ReleaseBuild build in unit.Builds)
        {
            string entry = parent.Length == 0 ? build.EntryPoint : parent + "/" + build.EntryPoint;
            ImpactPlanner.ValidatePath(entry);
            string directory = NodePackageChecks.Parent(entry);
            if (directory.Length == 0) directory = ".";
            if (build.Definition != Build ||
                !sources.TryGetValue(directory, out RubySourceGemFacts? source) ||
                !Path.IsPathFullyQualified(source.Gemspec) ||
                !PathComparison.Equals(Path.GetFullPath(Path.Combine(root, entry)),
                    Path.GetFullPath(source.Gemspec)) ||
                !RubyReleaseUnits.Registered(source.Gem.Name) ||
                string.IsNullOrWhiteSpace(source.Gem.Version) || source.Gem.Platform != "ruby")
                throw new InvalidDataException("Unresolved adopted native Ruby gem subject.");
            if (build.Outputs is not [{ Role: "primary-package", Kind: "ruby-gem" }])
                throw new InvalidDataException("Unsupported complete Ruby gem output set.");
            var subject = new PackageTarget(unit.Id, unit.SourcePath, build.Id, build.Definition,
                directory, entry, null, source.Gem.Version, build.Outputs.Select(output =>
                    new PackageOutput(output.Id, output.Role, output.Kind)).ToArray());
            string target = "release/" + unit.Id + "/" + build.Id;
            var pack = new CheckKey(target, Build, "default");
            var contents = new CheckKey(target, Contents, "default");
            checks.Add(new(pack, [], "ubuntu-latest", true, [], subject));
            checks.Add(new(contents, [], "ubuntu-latest", true, [pack], subject));
            checks.Add(new(new(target, Consumer, "default"), [], "ubuntu-latest", true,
                [pack, contents], subject));
        }
        return [.. checks];
    }

    private static StringComparer PathComparison => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
