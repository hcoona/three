using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record RubyRepositoryRequest(RubyFactTools Tools, string Nbgv,
    string BundlerProbeVersion);
internal sealed record RubyRevisionInputs(RubyFactsEndpoint Facts, RubySourceGemFacts[] Sources,
    RubyBundleFacts[] Bundles, ReleaseUnitDeclaration[] Units);

// The caller prepares dependencies. Native libraries evaluate every original entry point.
internal static class RubyRepositoryReader
{
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
        token.ThrowIfCancellationRequested();
        return new(new(checkout.Revision, inputs, owners.ToArray(), operationInputs.ToArray()),
            sources, bundles.ToArray(), units.ToArray());
    }
}
