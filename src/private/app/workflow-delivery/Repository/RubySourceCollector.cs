using System.Text.Json;
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
        var environment = new Dictionary<string, string?>(tools.Environment,
            StringComparer.Ordinal) { ["THREE_WORKFLOW_RELEASE_NBGV_PATH"] = nbgv };
        var collector = new RubyNativeFactCollector(tools with { Environment = environment },
            execute);
        var sources = new List<RubySourceGemFacts>();
        foreach (RubySourceLocation source in inventory.Sources)
        {
            token.ThrowIfCancellationRequested();
            string name = Path.GetFileNameWithoutExtension(source.Gemspec);
            if (source.Directory == "." ||
                name is not ("asciidoctor-latexmath" or "hcoona-release-smoke-ruby"))
                throw new InvalidDataException("Unsupported native Ruby source input scope.");
            string project = Path.Combine(checkout.Root, source.Directory);
            string gemspec = Path.Combine(checkout.Root, source.Gemspec);
            NativeCommandResult result = await execute(new(nbgv, project,
                ["get-version", "--format", "json"], tools.DeadlineSeconds,
                environment, InheritEnvironment: false), token);
            token.ThrowIfCancellationRequested();
            if (!result.Succeeded || result.Error is not null)
                throw new InvalidDataException("Native Ruby source version query failed.");
            string raw = SemVer2(result.Stdout);
            RubyVersionProjection version = await collector.VersionAsync(gemspec, raw, token);
            if (name == "hcoona-release-smoke-ruby")
            {
                string path = source.Directory + "/lib/hcoona_release_smoke_ruby/version.rb";
                if (checkout.Revision.Entries.Any(entry => entry.Path == path))
                    throw new InvalidDataException("Generated Ruby version is a committed input.");
                string output = Path.Combine(checkout.Root, path);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                await File.WriteAllTextAsync(output,
                    "# frozen_string_literal: true\n\nmodule HcoonaReleaseSmokeRuby\n" +
                    "  VERSION = " + version.Literal + "\nend\n", token);
            }
            RubySourceGemFacts facts = await collector.SourceAsync(gemspec, token);
            if (facts.Gem.Name != name || facts.Gem.Version != version.Version)
                throw new InvalidDataException("Native gemspec differs from its NBGV projection.");
            sources.Add(facts);
        }
        return [.. sources];
    }

    private static string SemVer2(string text)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            string? version = document.RootElement.GetProperty("SemVer2").GetString();
            return !string.IsNullOrWhiteSpace(version) ? version :
                throw new InvalidDataException("Missing native NBGV SemVer2.");
        }
        catch (Exception error) when (error is JsonException or KeyNotFoundException or
            InvalidOperationException)
        {
            throw new InvalidDataException("Incomplete native NBGV version answer.", error);
        }
    }
}
