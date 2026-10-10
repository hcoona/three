using System.Text.Json;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record RubyFactTools(string Ruby, string Helper, string Scratch,
    IReadOnlyDictionary<string, string?> Environment, int DeadlineSeconds);

// The caller prepares native tools. RubyGems/Bundler evaluate; the typed reader binds answers.
internal sealed class RubyNativeFactCollector
{
    private readonly RubyFactTools tools;
    private readonly Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute;

    internal RubyNativeFactCollector(RubyFactTools tools)
        : this(tools, NativeProcess.ExecuteAsync) { }

    internal RubyNativeFactCollector(RubyFactTools tools,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute)
    {
        this.tools = tools;
        this.execute = execute;
        Absolute(tools.Ruby);
        Absolute(tools.Helper);
        Absolute(tools.Scratch);
        if (tools.DeadlineSeconds <= 0)
            throw new InvalidDataException("Ruby collection requires a positive deadline.");
    }

    internal async Task<RubySourceGemFacts> SourceAsync(string gemspec, CancellationToken token) =>
        RubyNativeFactsReader.ParseSource(await QueryAsync("gemspec", gemspec, null, token),
            gemspec);

    internal async Task<RubyArchiveFacts> ArchiveAsync(string archive, CancellationToken token) =>
        RubyNativeFactsReader.ParseArchive(await QueryAsync("archive", archive, null, token),
            archive);

    internal async Task<RubyBundleFacts> BundleAsync(string gemfile, string bundlerVersion,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(bundlerVersion))
            throw new InvalidDataException("Bundle collection requires its selected Bundler.");
        return RubyNativeFactsReader.ParseBundle(
            await QueryAsync("bundle", gemfile, bundlerVersion, token), gemfile, bundlerVersion);
    }

    private async Task<string> QueryAsync(string operation, string path, string? bundlerVersion,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Absolute(path);
        string request = Path.Combine(tools.Scratch, "ruby-facts-" + Guid.NewGuid() + ".json");
        try
        {
            await File.WriteAllTextAsync(request, JsonSerializer.Serialize(new { operation, path }),
                token);
            var environment = new Dictionary<string, string?>(tools.Environment,
                StringComparer.Ordinal);
            string[] arguments;
            if (bundlerVersion is null)
                arguments = [tools.Helper, request];
            else
            {
                environment["BUNDLE_GEMFILE"] = path;
                environment["BUNDLE_FROZEN"] = "1";
                arguments = ["-e", "gem \"bundler\", ARGV.shift; load ARGV.shift",
                    bundlerVersion, tools.Helper, request];
            }
            NativeCommandResult result = await execute(new(tools.Ruby,
                Path.GetDirectoryName(path)!, arguments, tools.DeadlineSeconds, environment,
                InheritEnvironment: false), token);
            token.ThrowIfCancellationRequested();
            if (!result.Succeeded || result.Error is not null)
                throw new InvalidDataException("Native Ruby fact collection failed: " +
                    result.Termination + ".");
            return result.Stdout;
        }
        finally
        {
            File.Delete(request);
        }
    }

    private static void Absolute(string path)
    {
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidDataException("Ruby collection requires absolute native paths.");
    }
}
