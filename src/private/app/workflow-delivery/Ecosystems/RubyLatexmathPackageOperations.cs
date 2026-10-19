using System.Security.Cryptography;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Ecosystems;

// The concrete asciidoctor-latexmath gem. RubyGems/Bundler own build, resolution and loading.
internal sealed class RubyLatexmathPackageOperations(RubyFactTools tools, string gem,
    string nbgv, string state, string project) : IRubyReleasePackage
{
    private const string Name = RubyReleaseUnits.Latexmath;
    private string[] expected = [];

    public async Task<RubyGemArchive> BuildAsync(GitMaterialization checkout, string directory,
        string entryPoint, string version,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        RubySourceGemFacts source = await RubySourceCollector.ReadSourceAsync(checkout,
            entryPoint, tools, nbgv, execute, token);
        if (source.Gem.Name != Name || source.Gem.Version != version ||
            source.Gem.Platform != "ruby")
            throw new InvalidDataException("Native gem source identity differs.");
        // The gemspec declares lib/**, README.md and LICENSE; the revision supplies them.
        string prefix = directory + "/";
        expected = [.. checkout.Revision.Entries
            .Where(entry => entry.Path.StartsWith(prefix + "lib/", StringComparison.Ordinal))
            .Select(entry => entry.Path[prefix.Length..]), "README.md", "LICENSE"];
        string archive = Path.Combine(state, "original.gem");
        var environment = new Dictionary<string, string?>(tools.Environment,
            StringComparer.Ordinal) { ["THREE_WORKFLOW_RELEASE_NBGV_PATH"] = nbgv };
        await RubySmokePackageOperations.RunAsync(new(tools.Ruby,
            Path.Combine(checkout.Root, directory),
            [gem, "build", "--norc", Path.Combine(checkout.Root, entryPoint), "--output", archive],
            tools.DeadlineSeconds, environment, InheritEnvironment: false,
            CloseStandardInput: true), execute, token);
        if (!File.Exists(archive))
            throw new InvalidDataException("Native gem build omitted its output.");
        return new RubyGemArchive(archive, OwnedFile.Length(archive),
            Convert.ToHexStringLower(SHA256.HashData(
                await File.ReadAllBytesAsync(archive, token))));
    }

    public async Task<RubyArchiveFacts> InspectReleaseAsync(RubyGemArchive original,
        string version, Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        RubySmokePackageOperations.RequireOriginal(original);
        RubyArchiveFacts facts = await new RubyNativeFactCollector(tools, execute)
            .ArchiveAsync(original.Path, token);
        if (facts.Gem.Name != Name || facts.Gem.Version != version ||
            facts.Gem.Platform != "ruby" || facts.Gem.RuntimeDependencies is not
            [{ Name: "asciidoctor", Requirement: ">= 2.0, < 3.0" }] ||
            facts.Extensions is not [] || facts.Repository != "https://github.com/hcoona/three" ||
            !facts.Gem.RequirePaths.SequenceEqual(["lib"]) || expected.Length == 0 ||
            !facts.Contents.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(
                StringComparer.Ordinal)))
            throw new InvalidDataException("Unsupported native Ruby Release archive.");
        RubySmokePackageOperations.RequireOriginal(original);
        return facts;
    }

    // Runtime dependencies come from the committed project lock; Bundler owns the install.
    public async Task ConsumeReleaseAsync(RubyGemArchive original, string version,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        RubySmokePackageOperations.RequireOriginal(original);
        string consumer = Directory.CreateDirectory(Path.Combine(state, "consumer")).FullName;
        string gems = Path.Combine(consumer, "gems");
        var environment = new Dictionary<string, string?>(tools.Environment,
            StringComparer.Ordinal)
        {
            ["GEM_HOME"] = gems,
            ["GEM_PATH"] = gems,
            ["RUBYLIB"] = null,
            ["RUBYOPT"] = null,
            ["BUNDLE_GEMFILE"] = Path.Combine(project, "Gemfile"),
            ["BUNDLE_FROZEN"] = "1",
            ["BUNDLE_WITHOUT"] = "development",
            ["BUNDLE_IGNORE_CONFIG"] = "1",
            ["BUNDLE_APP_CONFIG"] = Path.Combine(consumer, "bundle-config"),
            ["BUNDLE_USER_HOME"] = Path.Combine(consumer, "bundle-home"),
        };
        NativeCommandResult locked = await execute(new(tools.Ruby, consumer,
            ["-r", "bundler", "-e", "puts Bundler::LockfileParser.bundled_with"],
            tools.DeadlineSeconds, environment, InheritEnvironment: false,
            CloseStandardInput: true), token);
        token.ThrowIfCancellationRequested();
        string bundler = locked.Stdout.Trim();
        if (!locked.Succeeded || locked.Error is not null || bundler.Length == 0)
            throw new IOException("Native locked Bundler query failed: " +
                NativeDiagnostic.Describe(locked));
        await RubySmokePackageOperations.RunAsync(new(tools.Ruby, consumer,
            [gem, "install", "--norc", "bundler", "--version", bundler, "--no-document"],
            tools.DeadlineSeconds, environment, InheritEnvironment: false,
            CloseStandardInput: true), execute, token);
        await RubySmokePackageOperations.RunAsync(new(tools.Ruby, consumer,
            [Path.Combine(gems, "bin", "bundle"), "_" + bundler + "_", "install"],
            tools.DeadlineSeconds, environment, InheritEnvironment: false,
            CloseStandardInput: true), execute, token);
        await RubySmokePackageOperations.RunAsync(new(tools.Ruby, consumer,
            [gem, "install", "--norc", original.Path, "--local", "--no-document"],
            tools.DeadlineSeconds, environment, InheritEnvironment: false,
            CloseStandardInput: true), execute, token);
        // RubyGems' native default path adds only the selected Ruby's own installed gems.
        var load = new Dictionary<string, string?>(environment, StringComparer.Ordinal)
        {
            ["GEM_PATH"] = null,
            ["BUNDLE_GEMFILE"] = null,
        };
        await RubySmokePackageOperations.RunAsync(new(tools.Ruby, consumer,
            ["-e", "require 'asciidoctor-latexmath'; " +
                "spec = Gem.loaded_specs.fetch('asciidoctor-latexmath'); " +
                "abort 'wrong installed specification' unless " +
                "spec.name == 'asciidoctor-latexmath' && " +
                "spec.version.to_s == ARGV.fetch(0) && spec.platform.to_s == 'ruby' && " +
                "Asciidoctor::Latexmath::VERSION == ARGV.fetch(0); " +
                "root = File.realpath(ARGV.fetch(1)) + File::SEPARATOR; " +
                "own = File.realpath(spec.full_gem_path) + File::SEPARATOR; " +
                "dependency = Gem.loaded_specs.fetch('asciidoctor'); " +
                "abort 'installed specification leaves owned state' unless " +
                "own.start_with?(root) && " +
                "File.realpath(dependency.full_gem_path).start_with?(root); " +
                "loaded = $LOADED_FEATURES.select { |path| " +
                "path.end_with?('/asciidoctor-latexmath.rb') || " +
                "path.include?('/asciidoctor/latexmath/') }; " +
                "abort 'wrong installed import origin' unless loaded.any? && " +
                "loaded.all? { |path| File.realpath(path).start_with?(own) }; " +
                "abort 'missing installed API' unless " +
                "Asciidoctor::Latexmath.respond_to?(:register)",
                version, gems],
            tools.DeadlineSeconds, load, InheritEnvironment: false,
            CloseStandardInput: true), execute, token);
        RubySmokePackageOperations.RequireOriginal(original);
    }
}
