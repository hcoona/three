using WorkflowDelivery.CI;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record RubyRepositoryRequest(RubyFactTools Tools, string Nbgv,
    string BundlerProbeVersion);
internal sealed record RubyRevisionInputs(RubyFactsEndpoint Facts, RubySourceGemFacts[] Sources,
    RubyBundleFacts[] Bundles, ReleaseUnitDeclaration[] Units);
internal sealed record RubyRepositoryInputs(RubyDependencyGraph Graph, SharedInput[] Inputs);

// The caller prepares dependencies. Native libraries evaluate every original entry point.
internal static class RubyRepositoryReader
{
    // Concrete root-CI operation inputs, separate from native producer membership.
    internal static readonly string[] CallerSources = [".github/workflows/ci.yml",
        "src/private/app/workflow-delivery/CI/Callers/RubyCiCaller.cs",
        "src/private/app/workflow-delivery/CI/Callers/NodeCiCaller.cs",
        "src/private/app/workflow-delivery/CI/Callers/DotNetCiCaller.cs",
        "eng/scripts/Bootstrap-CiControl.ps1",
        "src/private/app/workflow-delivery/CI/Callers/CiCallerCli.cs",
        "src/private/app/workflow-delivery/CI/Callers/CiCallerTransfer.cs",
        "src/private/app/workflow-delivery/CI/Callers/ControlCiCaller.cs",
        "mise.toml", "mise.lock", "eng/scripts/locked_tool_versions.py",
        "eng/dotnet-frameworks/mise.toml", "eng/dotnet-frameworks/mise.lock"];

    // A release unit keeps the quality obligations of its registered recipe. Only the
    // candidate is held to the current registration; a basis keeps its own adoption.
    internal static void RequireRegisteredPresets(RubyRevisionInputs inputs)
    {
        foreach (ReleaseUnitDeclaration unit in inputs.Units)
            _ = ReleaseUnitCatalog.Ruby(unit.Id);
        foreach (ProjectFacts owner in inputs.Facts.Owners.Where(owner =>
                     owner.ReleaseUnit is not null))
            if (ReleaseUnitCatalog.Ruby(inputs.Facts.Native.Graph.Projects.Single(project =>
                    project.Directory == owner.Directory).Name).Preset != owner.QualityPreset)
                throw new InvalidDataException("Unsupported adopted Ruby quality obligations.");
    }

    internal static Task<RubyRevisionInputs> ReadAsync(GitMaterialization checkout,
        RubyRepositoryRequest request, CancellationToken token) =>
        ReadAsync(checkout, request, NativeProcess.ExecuteAsync, token);

    internal static async Task<RubyRevisionInputs> ReadAsync(GitMaterialization checkout,
        RubyRepositoryRequest request,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        RubyRepositoryInventory inventory = RubyRepositoryInventory.Discover(checkout.Revision);
        RubySourceGemFacts[] sources = await RubySourceCollector.ReadAsync(checkout, request.Tools,
            request.Nbgv, execute, token);
        var collector = new RubyNativeFactCollector(request.Tools, execute);
        var bundles = new List<RubyBundleFacts>();
        foreach (RubyBundleLocation bundle in inventory.Bundles)
        {
            string gemfile = Path.Combine(checkout.Root, bundle.Gemfile);
            RubyBundleContext context = await collector.ContextAsync(gemfile,
                request.BundlerProbeVersion, token);
            // Bundler selects its locked library; the caller has installed that context.
            bundles.Add(await collector.BundleAsync(gemfile, context.LockedBundlerVersion, token));
        }
        var quality = new Dictionary<string, QualitySelection?>(StringComparer.Ordinal);
        var versions = new List<NbgvInputs>();
        var nbgv = new NbgvInputReader(checkout.Root);
        var selections = new QualitySelectionReader(checkout.Root, checkout.ReadOptionalTextAsync);
        foreach (RubySourceLocation source in inventory.Sources)
        {
            quality.Add(source.Directory,
                await selections.ReadAsync(source.Directory, "ruby", token));
            versions.Add(nbgv.Read(checkout.Revision, source.Directory, token));
        }
        RubyRepositoryInputs inputs = MapInputs(checkout.Root, checkout.Revision,
            sources, bundles.ToArray(), versions.ToArray(), quality);
        var native = sources.ToDictionary(source => Path.GetRelativePath(checkout.Root,
            Path.GetDirectoryName(source.Gemspec)!).Replace('\\', '/'), StringComparer.Ordinal);
        var units = new List<ReleaseUnitDeclaration>();
        var membership = new Dictionary<string, ReleaseUnitDeclaration>(StringComparer.Ordinal);
        foreach (GitEntry entry in checkout.Revision.Entries.Where(entry =>
                     entry.Path == "workflow-delivery.release-unit.yml" ||
                     entry.Path.EndsWith("/workflow-delivery.release-unit.yml",
                         StringComparison.Ordinal)))
        {
            ReleaseUnitDeclaration unit = ReleaseUnitDeclarationReader.Read(
                (await checkout.ReadOptionalTextAsync(entry.Path, token))!, entry.Path);
            if (!unit.Builds.Any(build => build.Definition.StartsWith("ruby/",
                    StringComparison.Ordinal))) continue;
            CheckSpec[] checks = RubyPackageChecks.Expand(unit, checkout.Root, native);
            foreach (string directory in checks.Select(check => check.Package!.Directory)
                         .Distinct(StringComparer.Ordinal))
                if (!membership.TryAdd(directory, unit))
                    throw new InvalidDataException(
                        "Ruby source belongs to multiple release units.");
            units.Add(unit);
        }
        var owners = new List<ProjectFacts>();
        var operationInputs = new List<SharedInput>();
        foreach (RubySourceLocation source in inventory.Sources)
        {
            QualitySelection? selection = quality[source.Directory];
            if (selection is null) continue;
            CheckSpec[] checks;
            ReleaseUnitDeclaration? unit = membership.GetValueOrDefault(source.Directory);
            if (selection.Preset == RubyProjectChecks.Preset)
                checks = RubyProjectChecks.Expand(selection, source.Directory);
            else if (RubyPackageChecks.Presets.Contains(selection.Preset) && unit is not null)
                checks = [.. selection.Preset == RubyPackageChecks.RSpecPreset
                        ? [RubyProjectChecks.Suite(source.Directory)] : Array.Empty<CheckSpec>(),
                    .. RubyPackageChecks.Expand(unit, checkout.Root, native)
                        .Where(check => check.Package!.Directory == source.Directory)];
            else
                throw new InvalidDataException("Unsupported adopted Ruby quality obligations.");
            string id = "ruby:" + source.Directory;
            owners.Add(new(id, source.Directory, [], [], unit?.Id, selection.Preset, checks));
            if (unit is not null) operationInputs.Add(new(unit.SourcePath, [id]));
        }
        // A declared native test suite needs a bundle, not a synthetic publishable gem.
        foreach (RubyBundleLocation bundle in inventory.Bundles)
        {
            if (native.ContainsKey(bundle.Directory) || owners.Any(owner =>
                    RubyFactsAssembler.Governs(owner.Directory, bundle.Directory, native.Keys)))
                continue;
            QualitySelection? selection =
                await selections.ReadAsync(bundle.Directory, "ruby", token);
            if (selection is null) continue;
            if (selection.Preset != RubyProjectChecks.Preset)
                throw new InvalidDataException("Bundle-only Ruby quality requires native RSpec.");
            string id = "ruby:" + bundle.Directory;
            string[] paths = checkout.Revision.Entries.Select(entry => entry.Path).Where(path =>
                path != bundle.Directory &&
                RubyFactsAssembler.Governs(bundle.Directory, path, native.Keys)).ToArray();
            owners.Add(new(id, bundle.Directory, [], [], null, selection.Preset,
                RubyProjectChecks.Expand(selection, bundle.Directory), paths));
            operationInputs.Add(new(selection.SourcePath, [id]));
            // Native evaluated inputs can include files outside the suite directory.
            foreach (RubyBundleConsumption context in inputs.Graph.Bundles.Where(context =>
                         RubyFactsAssembler.Governs(bundle.Directory, context.Directory,
                             native.Keys)))
                foreach (string path in context.Inputs) operationInputs.Add(new(path, [id]));
            if (bundle.Directory == RubyNativeTestInputs.Directory)
                foreach (string path in checkout.Revision.Entries.Select(entry => entry.Path)
                             .Where(RubyNativeTestInputs.Consumes))
                    operationInputs.Add(new(path, [id]));
        }
        string[] consumers = owners.Select(owner => owner.Id).ToArray();
        foreach (string path in CallerSources)
        {
            GitEntry? entry = checkout.Revision.Entries.SingleOrDefault(value =>
                value.Path == path);
            if (entry is not null) RubyRepositoryInventory.RequireFile(entry);
            operationInputs.Add(new(path, consumers));
        }
        token.ThrowIfCancellationRequested();
        return new(new(checkout.Revision, inputs, owners.ToArray(), operationInputs.ToArray()),
            sources, bundles.ToArray(), units.ToArray());
    }

    private static RubyRepositoryInputs MapInputs(string root, GitRevision revision,
        RubySourceGemFacts[] sources, RubyBundleFacts[] bundles, NbgvInputs[] versions,
        IReadOnlyDictionary<string, QualitySelection?> quality)
    {
        RubyRepositoryInventory inventory = RubyRepositoryInventory.Discover(revision);
        RubyDependencyGraph graph = inventory.Bind(root, sources, bundles);
        var projects = graph.Projects.Select(project => project.Directory)
            .ToHashSet(StringComparer.Ordinal);
        if (projects.Contains("."))
            throw new InvalidDataException("Root Ruby source input ownership is unsupported.");
        if (quality.Count != projects.Count || quality.Keys.Any(key => !projects.Contains(key)))
            throw new InvalidDataException("Ruby quality owners do not match source producers.");
        var entries = revision.Entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        var inputs = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (GitEntry entry in revision.Entries)
        {
            string? owner = GitPathOwnership.NearestDirectory(entry.Path, projects);
            if (owner is null) continue;
            RubyRepositoryInventory.RequireFile(entry);
            Add(entry.Path, owner);
        }
        var versioned = new HashSet<string>(StringComparer.Ordinal);
        foreach (NbgvInputs version in versions)
        {
            if (version.Commit != revision.Commit || !projects.Contains(version.Directory) ||
                !versioned.Add(version.Directory))
                throw new InvalidDataException("Unresolved Ruby version input owner or revision.");
            foreach (string path in version.Paths)
            {
                Require(path);
                Add(path, version.Directory);
            }
            foreach (string path in version.ConfigurationCandidates)
                Add(path, version.Directory);
        }
        foreach (string project in projects)
        {
            QualitySelection? selection = quality[project];
            if (selection is not null) Require(selection.SourcePath);
            string directory = project;
            while (true)
            {
                string path = (directory.Length == 0 ? "" : directory + "/") +
                    "workflow-delivery.quality.yml";
                Add(path, project);
                if (path == selection?.SourcePath) break;
                if (directory.Length == 0)
                {
                    if (selection is not null)
                        throw new InvalidDataException(
                            "Ruby quality is outside its ancestor search.");
                    break;
                }
                int separator = directory.LastIndexOf('/');
                directory = separator < 0 ? "" : directory[..separator];
            }
        }
        foreach (RubyBundleConsumption bundle in graph.Bundles)
            foreach (string path in bundle.Inputs)
            {
                Require(path);
                if (projects.Contains(bundle.Directory)) Add(path, bundle.Directory);
            }
        return new(graph, GitPathOwnership.GroupSharedInputs(inputs));

        void Require(string path)
        {
            if (!entries.TryGetValue(path, out GitEntry? entry))
                throw new InvalidDataException("A required Ruby input is not committed.");
            RubyRepositoryInventory.RequireFile(entry);
        }

        void Add(string path, string consumer)
        {
            ImpactPlanner.ValidatePath(path);
            if (entries.TryGetValue(path, out GitEntry? entry))
                RubyRepositoryInventory.RequireFile(entry);
            if (!inputs.TryGetValue(path, out HashSet<string>? owners))
                inputs.Add(path, owners = new(StringComparer.Ordinal));
            owners.Add(consumer);
        }
    }
}

internal sealed record RubySourceLocation(string Directory, string Gemspec);
internal sealed record RubyBundleLocation(string Directory, string Gemfile);
internal sealed record RubyRepositoryInventory(RubySourceLocation[] Sources,
    RubyBundleLocation[] Bundles)
{
    // Git discovers committed entry points; native libraries still interpret their contents.
    internal static RubyRepositoryInventory Discover(GitRevision revision)
    {
        var sources = new Dictionary<string, RubySourceLocation>(StringComparer.Ordinal);
        var bundles = new Dictionary<string, RubyBundleLocation>(StringComparer.Ordinal);
        foreach (GitEntry entry in revision.Entries)
        {
            bool source = entry.Path.EndsWith(".gemspec", StringComparison.Ordinal);
            string name = entry.Path[(entry.Path.LastIndexOf('/') + 1)..];
            bool bundle = name is "Gemfile" or "gems.rb";
            if (!source && !bundle) continue;
            RequireFile(entry);
            int separator = entry.Path.LastIndexOf('/');
            string directory = separator < 0 ? "." : entry.Path[..separator];
            if (source && !sources.TryAdd(directory, new(directory, entry.Path)))
                throw new InvalidDataException("Ambiguous source gemspec directory.");
            if (bundle && !bundles.TryAdd(directory, new(directory, entry.Path)))
                throw new InvalidDataException("Ambiguous bundle directory.");
        }
        return new(sources.Values.OrderBy(source => source.Directory,
            StringComparer.Ordinal).ToArray(), bundles.Values.OrderBy(bundle => bundle.Directory,
            StringComparer.Ordinal).ToArray());
    }

    internal RubyDependencyGraph Bind(string root, RubySourceGemFacts[] sources,
        RubyBundleFacts[] bundles)
    {
        if (!Path.IsPathFullyQualified(root))
            throw new InvalidDataException("Ruby input binding requires its absolute endpoint.");
        RequireSame(Sources.Select(source => source.Gemspec),
            sources.Select(source => Relative(root, source.Gemspec)));
        RequireSame(Bundles.Select(bundle => bundle.Gemfile),
            bundles.Select(bundle => Relative(root, bundle.Gemfile)));
        return RubyDependencyProjection.Project(root, sources, bundles);
    }

    internal static void RequireFile(GitEntry entry)
    {
        ImpactPlanner.ValidatePath(entry.Path);
        if (entry.ObjectType != "blob" || entry.Mode is not ("100644" or "100755"))
            throw new InvalidDataException("Ruby inputs require committed regular files.");
    }

    private static string Relative(string root, string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidDataException("Ruby facts require absolute native source paths.");
        string relative = Path.GetRelativePath(root, path)
            .Replace(Path.DirectorySeparatorChar, '/');
        ImpactPlanner.ValidatePath(relative);
        return relative;
    }

    private static void RequireSame(IEnumerable<string> expected, IEnumerable<string> actual)
    {
        string[] values = actual.ToArray();
        if (values.Distinct(StringComparer.Ordinal).Count() != values.Length ||
            !expected.Order(StringComparer.Ordinal).SequenceEqual(
                values.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidDataException(
                "Native Ruby answers omit or substitute an entry point.");
    }
}

// Concrete native helper tests consume their implementation, generated wire and product fixture.
internal static class RubyNativeTestInputs
{
    internal const string Directory = "tests/private/app/workflow-delivery/Native/Ruby";
    internal static bool Consumes(string path) =>
        path.StartsWith("src/private/app/workflow-delivery/Native/Ruby/",
            StringComparison.Ordinal) ||
        (path.StartsWith("src/private/app/workflow-delivery/Native/contracts/ruby-",
            StringComparison.Ordinal) && path.EndsWith(".schema.json", StringComparison.Ordinal)) ||
        path.StartsWith("tests/private/app/workflow-delivery/fixtures/products/" +
            "hcoona-release-smoke-ruby/", StringComparison.Ordinal) ||
        // The pin contract requires every committed lock's Bundler to be a supported context.
        path == "Gemfile.lock" || path.EndsWith("/Gemfile.lock", StringComparison.Ordinal);
}
