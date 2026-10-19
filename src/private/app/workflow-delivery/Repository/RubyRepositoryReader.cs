using WorkflowDelivery.CI;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record RubyRepositoryRequest(RubyFactTools Tools, string Nbgv,
    string BundlerProbeVersion);
internal sealed record RubyRevisionInputs(RubyFactsEndpoint Facts, RubySourceGemFacts[] Sources,
    RubyBundleFacts[] Bundles, ReleaseUnitDeclaration[] Units);

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
        "mise.toml", "mise.lock"];

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
        RubyRepositoryInputs inputs = RubyInputConsumers.Map(checkout.Root, checkout.Revision,
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
            // A release unit keeps the quality obligations of its registered recipe.
            if (unit is not null &&
                RubyReleaseUnits.Named(native[source.Directory].Gem.Name).Preset !=
                selection.Preset)
                throw new InvalidDataException("Unsupported adopted Ruby quality obligations.");
            if (selection.Preset == RubyProjectChecks.Preset)
                checks = RubyProjectChecks.Expand(selection, source.Directory);
            else if (selection.Preset == RubyPackageChecks.Preset && unit is not null)
                checks = RubyPackageChecks.Expand(unit, checkout.Root, native)
                    .Where(check => check.Package!.Directory == source.Directory).ToArray();
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
}
