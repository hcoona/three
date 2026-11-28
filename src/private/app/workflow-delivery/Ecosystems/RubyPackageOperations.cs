using WorkflowDelivery.CI;
using WorkflowDelivery.Core;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Ecosystems;

internal sealed record RubyGemArchive(string Path, long Length, string Sha256);
internal sealed record RubyPackageRecipe(string Id, string Require, bool NbgvEnv, bool LockedBundle,
    string[] Dependencies, string Probe, bool RSpec);

// RubyGems and Bundler own package semantics; callers own plans and command/result recording.
internal sealed class RubyPackageOperations(RubyPackageRecipe unit, RubyFactTools tools, string gem,
    string nbgv, string state)
{
    // Bundle installation and the whole suite each stay within preparation's overall cap.
    private const int SuiteDeadlineSeconds = 600;

    internal async Task<QualifiedUnitOriginals?> QualifyAsync(UnitBuildSubject subject,
        GitMaterialization checkout, string directory, string entryPoint, string version,
        Func<RubyRSpecRequest, Task> runSuite, RecordNativeCheck check,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        string project = Path.Combine(checkout.Root, directory);
        RubyGemArchive? original = null;
        QualifiedUnitOriginals? result = null;
        await check("build", async () =>
        {
            RubySourceLocation source = RubyRepositoryInventory.Discover(checkout.Revision).Sources
                .Single(item => item.Gemspec == entryPoint);
            RubySourceGemFacts facts =
                await RubySourceFacts.ReadAsync(checkout, source, tools, nbgv,
                execute, token);
            original =
                await BuildAsync(checkout, directory, entryPoint, version, facts, execute, token);
        });
        await check("contents", async () => _ = await InspectAsync(checkout, directory, original!,
            version, release: true, execute, token));
        await check("consumer", () => ConsumeAsync(project, original!, version, release: true,
            execute, token));
        if (unit.RSpec) await check("tests", async () => await runSuite(
            await PrepareSuiteAsync(checkout.Root, project, execute, token)));
        await check("original-bytes", () =>
        {
            RequireOriginal(original!);
            result = subject.Complete(new UnitOriginal(subject.ForKind("ruby-gem"), unit.Id,
                version, original!.Path, original.Length, original.Sha256));
            return Task.CompletedTask;
        });
        return result;
    }

    internal async Task<RubyGemArchive> BuildAsync(GitMaterialization checkout, string directory,
        string entryPoint, string version, RubySourceGemFacts source,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        string gemspec = Path.GetFullPath(entryPoint, checkout.Root);
        if (!gemspec.Equals(source.Gemspec, OwnedPath.Comparison) ||
            source.Gem.Name != unit.Id || source.Gem.Version != version ||
            source.Gem.Platform != "ruby")
            throw new InvalidDataException("Native gem source identity differs.");
        IReadOnlyDictionary<string, string?> environment = unit.NbgvEnv
            ? new Dictionary<string, string?>(tools.Environment, StringComparer.Ordinal)
            { ["THREE_WORKFLOW_RELEASE_NBGV_PATH"] = nbgv }
            : tools.Environment;
        string archive = Path.Combine(state, "original.gem");
        await RunAsync(new(tools.Ruby, Path.Combine(checkout.Root, directory),
            [gem, "build", "--norc", gemspec, "--output", archive],
            tools.DeadlineSeconds, environment, InheritEnvironment: false,
            CloseStandardInput: true), execute, token);
        (long length, string sha256) = await OwnedFile.MeasureAsync(archive, token,
            "Native gem build omitted its output.");
        var original = new RubyGemArchive(archive, length, sha256);
        if (!unit.LockedBundle)
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
        string prefix = directory + "/";
        string[] expected = unit.LockedBundle
            ? [.. checkout.Revision.Entries
                .Where(entry => entry.Path.StartsWith(prefix + "lib/", StringComparison.Ordinal))
                .Select(entry => entry.Path[prefix.Length..]), "README.md", "CHANGELOG.md",
                "LICENSE"]
            : ["LICENSE", "README.md", $"lib/{unit.Require}.rb", $"lib/{unit.Require}/version.rb"];
        RubyDependency[] expectedDeps = [.. unit.Dependencies.Select(spec =>
        {
            string[] parts = spec.Split('@', 2);
            return new RubyDependency(parts[0], parts.Length > 1 ? parts[1] : ">= 0");
        })];
        bool runtimeMatches = facts.Gem.RuntimeDependencies.SequenceEqual(expectedDeps);
        bool qualified = release || unit.LockedBundle;
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
        Dictionary<string, string?> environment;
        if (unit.LockedBundle)
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
        if (unit.LockedBundle)
        {
            var load = new Dictionary<string, string?>(environment, StringComparer.Ordinal)
            {
                ["GEM_PATH"] = null,
                ["BUNDLE_GEMFILE"] = null,
            };
            await RunAsync(new(tools.Ruby, consumer,
                ["-e", $"require '{unit.Require}'; " +
                    $"spec = Gem.loaded_specs.fetch('{unit.Id}'); " +
                    "abort 'wrong installed specification' unless " +
                    $"spec.name == '{unit.Id}' && " +
                    "spec.version.to_s == ARGV.fetch(0) && spec.platform.to_s == 'ruby'; " +
                    "root = File.realpath(ARGV.fetch(1)) + File::SEPARATOR; " +
                    "own = File.realpath(spec.full_gem_path) + File::SEPARATOR; " +
                    "spec.runtime_dependencies.each { |dep| " +
                    "d = Gem.loaded_specs.fetch(dep.name); " +
                    "abort 'installed specification leaves owned state' unless " +
                    "File.realpath(d.full_gem_path).start_with?(root) }; " +
                    "abort 'installed specification leaves owned state' unless " +
                    "own.start_with?(root); " +
                    "loaded = $LOADED_FEATURES.select { |path| " +
                    $"path.end_with?('/{unit.Require}.rb') || " +
                    $"path.include?('/{unit.Require.Replace('-', '/')}/') }}; " +
                    "abort 'wrong installed import origin' unless loaded.any? && " +
                    "loaded.all? { |path| File.realpath(path).start_with?(own) }; " +
                    $"abort 'missing installed API' unless ({unit.Probe})",
                    version, gems],
                tools.DeadlineSeconds, load, InheritEnvironment: false,
                CloseStandardInput: true), execute, token);
        }
        else
        {
            await RunAsync(new(tools.Ruby, consumer,
                ["-e", $"require '{unit.Require}'; " +
                    $"abort 'wrong installed identity' unless ({unit.Probe}); " +
                    (release ? $"spec = Gem.loaded_specs.fetch('{unit.Id}'); " +
                        "abort 'wrong installed specification' unless " +
                        $"spec.name == '{unit.Id}' && " +
                        "spec.version.to_s == ARGV.fetch(0) && spec.platform.to_s == 'ruby'; " +
                        "root = File.realpath(ARGV.fetch(1)) + File::SEPARATOR; " +
                        "abort 'installed specification leaves owned state' unless " +
                        "File.realpath(spec.full_gem_path).start_with?(root); " +
                        "loaded = $LOADED_FEATURES.select { |path| " +
                        $"path.end_with?('/{unit.Require}.rb') || " +
                        $"path.end_with?('/{unit.Require}/version.rb') }}; " +
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
    internal async Task<RubyRSpecRequest> PrepareSuiteAsync(
        string checkout,
        string projectDirectory,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        if (!unit.RSpec)
            throw new InvalidDataException("Ruby Release unit has no adopted suite.");
        string tests = Directory.CreateDirectory(Path.Combine(state, "tests")).FullName;
        (Dictionary<string, string?> environment, string bundler) = await InstallLockedAsync(
            projectDirectory, tests, null, execute, token);
        return new(checkout, tools.Ruby, Path.Combine(tests, "gems", "bin", "bundle"), bundler,
            Path.Combine(projectDirectory, "Gemfile"), tests, SuiteDeadlineSeconds, environment);
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
