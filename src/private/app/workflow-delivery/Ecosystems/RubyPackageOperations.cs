using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Ecosystems;

internal sealed record RubyGemArchive(string Path, long Length, string Sha256);

// RubyGems and Bundler own package semantics; callers own plans and command/result recording.
internal sealed class RubyPackageOperations(RubyReleaseUnit unit, RubyFactTools tools, string gem,
    string nbgv, string state)
{
    // Bundle installation and the whole suite each stay within preparation's overall cap.
    private const int SuiteDeadlineSeconds = 600;

    internal async Task<RubyGemArchive> BuildAsync(GitMaterialization checkout, string directory,
        string entryPoint, string version,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        RubySourceGemFacts source = await RubySourceCollector.ReadSourceAsync(checkout,
            entryPoint, tools, nbgv, execute, token);
        if (source.Gem.Name != unit.Id || source.Gem.Version != version ||
            source.Gem.Platform != "ruby")
            throw new InvalidDataException("Native gem source identity differs.");
        IReadOnlyDictionary<string, string?> environment = unit.Id == RubyReleaseUnits.Latexmath
            ? new Dictionary<string, string?>(tools.Environment, StringComparer.Ordinal)
            { ["THREE_WORKFLOW_RELEASE_NBGV_PATH"] = nbgv }
            : tools.Environment;
        string archive = Path.Combine(state, "original.gem");
        await RunAsync(new(tools.Ruby, Path.Combine(checkout.Root, directory),
            [gem, "build", "--norc", Path.Combine(checkout.Root, entryPoint), "--output", archive],
            tools.DeadlineSeconds, environment, InheritEnvironment: false,
            CloseStandardInput: true), execute, token);
        (long length, string sha256) = await OwnedFile.MeasureAsync(archive, token,
            "Native gem build omitted its output.");
        var original = new RubyGemArchive(archive, length, sha256);
        if (unit.Id == RubyReleaseUnits.Smoke)
            await InspectAsync(checkout, directory, original, version, release: false, execute,
                token);
        return original;
    }

    internal async Task<RubyArchiveFacts> InspectAsync(GitMaterialization checkout,
        string directory, RubyGemArchive original, string version, bool release,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        RequireOriginal(original);
        RubyArchiveFacts facts = await new RubyNativeFactCollector(tools, execute)
            .ArchiveAsync(original.Path, token);
        bool latexmath = unit.Id == RubyReleaseUnits.Latexmath;
        string prefix = directory + "/";
        // The Latexmath gemspec declares lib/**, README.md and LICENSE from the revision.
        string[] expected = latexmath
            ? [.. checkout.Revision.Entries
                .Where(entry => entry.Path.StartsWith(prefix + "lib/", StringComparison.Ordinal))
                .Select(entry => entry.Path[prefix.Length..]), "README.md", "LICENSE"]
            : ["LICENSE", "README.md", "lib/hcoona_release_smoke_ruby.rb",
                "lib/hcoona_release_smoke_ruby/version.rb"];
        bool runtimeMatches = latexmath
            ? facts.Gem.RuntimeDependencies is
                [{ Name: "asciidoctor", Requirement: ">= 2.0, < 3.0" }]
            : facts.Gem.RuntimeDependencies is [];
        bool qualified = release || latexmath;
        if (facts.Gem.Name != unit.Id || facts.Gem.Version != version ||
            facts.Gem.Platform != "ruby" || !runtimeMatches ||
            !facts.Contents.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(
                StringComparer.Ordinal)) ||
            qualified && (facts.Extensions is not [] ||
                facts.Repository != "https://github.com/hcoona/three" ||
                !facts.Gem.RequirePaths.SequenceEqual(["lib"])))
            throw new InvalidDataException(qualified ? "Unsupported native Ruby Release archive."
                : "Native gem archive differs from its subject.");
        RequireOriginal(original);
        return facts;
    }

    internal async Task ConsumeAsync(string projectDirectory, RubyGemArchive original,
        string version, bool release,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        RequireOriginal(original);
        string consumer = Directory.CreateDirectory(Path.Combine(state, "consumer")).FullName;
        string gems = Path.Combine(consumer, "gems");
        bool latexmath = unit.Id == RubyReleaseUnits.Latexmath;
        Dictionary<string, string?> environment;
        if (latexmath)
            (environment, _) = await InstallLockedAsync(projectDirectory, consumer, "development",
                execute, token);
        else
            environment = new(tools.Environment, StringComparer.Ordinal)
            {
                ["GEM_HOME"] = gems,
                ["GEM_PATH"] = gems,
                ["RUBYLIB"] = null,
                ["RUBYOPT"] = null,
            };
        await RunAsync(new(tools.Ruby, consumer,
            [gem, "install", "--norc", original.Path, "--local", "--no-document"],
            tools.DeadlineSeconds, environment, InheritEnvironment: false,
            CloseStandardInput: true), execute, token);
        if (latexmath)
        {
            // RubyGems' native default path adds only the selected Ruby's own installed gems.
            var load = new Dictionary<string, string?>(environment, StringComparer.Ordinal)
            {
                ["GEM_PATH"] = null,
                ["BUNDLE_GEMFILE"] = null,
            };
            await RunAsync(new(tools.Ruby, consumer,
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
        }
        else
        {
            await RunAsync(new(tools.Ruby, consumer,
                ["-e", "require 'hcoona_release_smoke_ruby'; " +
                    "abort 'wrong installed identity' unless " +
                    "HcoonaReleaseSmokeRuby.project_id == 'hcoona-release-smoke-ruby' " +
                    "&& HcoonaReleaseSmokeRuby::VERSION == ARGV.fetch(0); " +
                    (release ? "spec = Gem.loaded_specs.fetch('hcoona-release-smoke-ruby'); " +
                        "abort 'wrong installed specification' unless " +
                        "spec.name == 'hcoona-release-smoke-ruby' && " +
                        "spec.version.to_s == ARGV.fetch(0) && spec.platform.to_s == 'ruby'; " +
                        "root = File.realpath(ARGV.fetch(1)) + File::SEPARATOR; " +
                        "abort 'installed specification leaves owned state' unless " +
                        "File.realpath(spec.full_gem_path).start_with?(root); " +
                        "loaded = $LOADED_FEATURES.select { |path| " +
                        "path.end_with?('/hcoona_release_smoke_ruby.rb') || " +
                        "path.end_with?('/hcoona_release_smoke_ruby/version.rb') }; " +
                        "abort 'wrong installed import origin' unless loaded.length == 2 && " +
                        "loaded.all? { |path| File.realpath(path).start_with?(" +
                        "File.realpath(spec.full_gem_path) + File::SEPARATOR) }" : ""),
                    version, gems],
                tools.DeadlineSeconds, environment, InheritEnvironment: false,
                CloseStandardInput: true), execute, token);
        }
        RequireOriginal(original);
    }

    // The adopted ruby/rspec-v1 suite runs from the exact checkout with its locked bundle.
    internal async Task TestReleaseAsync(GitMaterialization checkout, string directory,
        string projectDirectory,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        if (unit.Preset != RubyPackageChecks.RSpecPreset)
            throw new InvalidDataException("Ruby Release unit has no adopted suite.");
        string tests = Directory.CreateDirectory(Path.Combine(state, "tests")).FullName;
        (Dictionary<string, string?> environment, string bundler) = await InstallLockedAsync(
            projectDirectory, tests, null, execute, token);
        RubyRSpecRun run = await RubyRSpecExecution.RunAsync(
            new(directory, RubyProjectChecks.RSpec, "default"),
            new(checkout.Root, tools.Ruby, Path.Combine(tests, "gems", "bin", "bundle"), bundler,
                Path.Combine(projectDirectory, "Gemfile"), tests, SuiteDeadlineSeconds,
                environment),
            execute, token);
        if (run.Status != CheckStatus.Passed)
            throw new InvalidDataException(run.ReportError ?? "Native RSpec suite did not " +
                "pass: " + run.Status + ". " + NativeDiagnostic.Describe(run.Result));
    }

    // Bundler owns frozen resolution from the committed lock into owned gem state.
    private async Task<(Dictionary<string, string?> Environment, string Bundler)>
        InstallLockedAsync(string projectDirectory, string root, string? without,
            Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
            CancellationToken token)
    {
        string gems = Path.Combine(root, "gems");
        var environment = new Dictionary<string, string?>(tools.Environment,
            StringComparer.Ordinal)
        {
            ["GEM_HOME"] = gems,
            ["GEM_PATH"] = gems,
            ["RUBYLIB"] = null,
            ["RUBYOPT"] = null,
            // Bundler installs into the owned GEM_HOME, not a caller's shared bundle path.
            ["BUNDLE_PATH"] = null,
            ["BUNDLE_GEMFILE"] = Path.Combine(projectDirectory, "Gemfile"),
            ["BUNDLE_FROZEN"] = "1",
            ["BUNDLE_WITHOUT"] = without,
            ["BUNDLE_IGNORE_CONFIG"] = "1",
            ["BUNDLE_APP_CONFIG"] = Path.Combine(root, "bundle-config"),
            ["BUNDLE_USER_HOME"] = Path.Combine(root, "bundle-home"),
        };
        NativeCommandResult locked = await execute(new(tools.Ruby, root,
            ["-r", "bundler", "-e", "puts Bundler::LockfileParser.bundled_with"],
            tools.DeadlineSeconds, environment, InheritEnvironment: false,
            CloseStandardInput: true), token);
        token.ThrowIfCancellationRequested();
        string bundler = locked.Stdout.Trim();
        if (!locked.Succeeded || locked.Error is not null || bundler.Length == 0)
            throw new IOException("Native locked Bundler query failed: " +
                NativeDiagnostic.Describe(locked));
        await RunAsync(new(tools.Ruby, root,
            [gem, "install", "--norc", "bundler", "--version", bundler, "--no-document"],
            tools.DeadlineSeconds, environment, InheritEnvironment: false,
            CloseStandardInput: true), execute, token);
        await RunAsync(new(tools.Ruby, root,
            [Path.Combine(gems, "bin", "bundle"), "_" + bundler + "_", "install"],
            SuiteDeadlineSeconds, environment, InheritEnvironment: false,
            CloseStandardInput: true), execute, token);
        return (environment, bundler);
    }

    internal static void RequireOriginal(RubyGemArchive original) =>
        OwnedFile.Verify(original.Path, original.Length, original.Sha256,
            "Original gem changed between required checks.");

    internal static async Task RunAsync(NativeCommand command,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        NativeCommandResult result = await execute(command, token);
        token.ThrowIfCancellationRequested();
        if (!result.Succeeded || result.Error is not null)
            throw new IOException("Native gem command failed: " +
                NativeDiagnostic.Describe(result));
    }
}
