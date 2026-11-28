using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

// Prepare only native metadata inputs. Selected build/check preparation remains separate.
internal static class RubySourceCollector
{
    internal static Task<RubySourceGemFacts[]> ReadAsync(GitMaterialization checkout,
        RubyFactTools tools, string nbgv, CancellationToken token) =>
        ReadAsync(checkout, tools, nbgv, NativeProcess.ExecuteAsync, token);

    internal static async Task<RubySourceGemFacts[]> ReadAsync(GitMaterialization checkout,
        RubyFactTools tools, string nbgv,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(nbgv))
            throw new InvalidDataException("Ruby source collection requires prepared native NBGV.");
        RubyRepositoryInventory inventory = RubyRepositoryInventory.Discover(checkout.Revision);
        var sources = new List<RubySourceGemFacts>();
        foreach (RubySourceLocation source in inventory.Sources)
            sources.Add(await ReadSourceAsync(checkout, source.Gemspec, tools, nbgv,
                execute, token));
        return [.. sources];
    }

    // Planning reads every producer; selected execution prepares only its original entry point.
    internal static async Task<RubySourceGemFacts> ReadSourceAsync(GitMaterialization checkout,
        string entryPoint, RubyFactTools tools, string nbgv,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!Path.IsPathFullyQualified(nbgv))
            throw new InvalidDataException("Ruby source collection requires prepared native NBGV.");
        RubySourceLocation source = RubyRepositoryInventory.Discover(checkout.Revision).Sources
            .Single(item => item.Gemspec == entryPoint);
        string name = Path.GetFileNameWithoutExtension(source.Gemspec);
        if (source.Directory == "." ||
            (!Ecosystems.ReleaseUnitCatalog.IsRubyRegistered(name) &&
             !await HasCommittedRubyDeclarationAsync(checkout, source.Gemspec, token)))
            throw new InvalidDataException("Unsupported native Ruby source input scope.");
        return await RubySourceFacts.ReadAsync(checkout, source, tools, nbgv, execute, token);
    }

    private static async Task<bool> HasCommittedRubyDeclarationAsync(GitMaterialization checkout,
        string gemspecPath, CancellationToken token)
    {
        foreach (GitEntry entry in checkout.Revision.Entries.Where(entry =>
                     entry.Path == "workflow-delivery.release-unit.yml" ||
                     entry.Path.EndsWith("/workflow-delivery.release-unit.yml",
                         StringComparison.Ordinal)))
        {
            string? text = await checkout.ReadOptionalTextAsync(entry.Path, token);
            if (text is null) continue;
            ReleaseUnitDeclaration decl = ReleaseUnitDeclarationReader.Read(text, entry.Path);
            string parent = NodePackageChecks.Parent(decl.SourcePath);
            foreach (ReleaseBuild build in decl.Builds.Where(b => b.Definition ==
                         RubyPackageChecks.Build))
            {
                string resolved = parent.Length == 0 ? build.EntryPoint :
                    parent + "/" + build.EntryPoint;
                if (resolved == gemspecPath) return true;
            }
        }
        return false;
    }
}
