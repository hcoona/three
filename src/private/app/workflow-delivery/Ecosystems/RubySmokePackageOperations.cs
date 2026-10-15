using System.Security.Cryptography;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Ecosystems;

internal sealed record RubyGemArchive(string Path, long Length, string Sha256);

// RubyGems owns package semantics; callers own their plans and command/result recording.
internal sealed class RubySmokePackageOperations(RubyFactTools tools, string gem, string nbgv,
    string state)
{
    internal async Task<RubyGemArchive> BuildAsync(GitMaterialization checkout, string directory,
        string entryPoint, string version,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        RubySourceGemFacts source = await RubySourceCollector.ReadSourceAsync(checkout,
            entryPoint, tools, nbgv, execute, token);
        if (source.Gem.Name != "hcoona-release-smoke-ruby" ||
            source.Gem.Version != version || source.Gem.Platform != "ruby")
            throw new InvalidDataException("Native gem source identity differs.");
        string archive = Path.Combine(state, "original.gem");
        await RunAsync(new(tools.Ruby, Path.Combine(checkout.Root, directory),
            [gem, "build", "--norc", Path.Combine(checkout.Root, entryPoint), "--output", archive],
            tools.DeadlineSeconds, tools.Environment, InheritEnvironment: false,
            CloseStandardInput: true), execute, token);
        if (!File.Exists(archive))
            throw new InvalidDataException("Native gem build omitted its output.");
        var original = new RubyGemArchive(archive, OwnedFile.Length(archive),
            Convert.ToHexStringLower(SHA256.HashData(
                await File.ReadAllBytesAsync(archive, token))));
        await InspectAsync(original, version, execute, token);
        return original;
    }

    internal async Task InspectAsync(RubyGemArchive original, string version,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        RequireOriginal(original);
        var collector = new RubyNativeFactCollector(tools, execute);
        RubyArchiveFacts facts = await collector.ArchiveAsync(original.Path, token);
        string[] expected = ["LICENSE", "README.md", "lib/hcoona_release_smoke_ruby.rb",
            "lib/hcoona_release_smoke_ruby/version.rb"];
        string[] actual = facts.Contents;
        if (facts.Gem.Name != "hcoona-release-smoke-ruby" || facts.Gem.Version != version ||
            facts.Gem.Platform != "ruby" || facts.Gem.RuntimeDependencies.Length != 0 ||
            !actual.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(
                StringComparer.Ordinal)))
            throw new InvalidDataException("Native gem archive differs from its subject.");
        RequireOriginal(original);
    }

    internal async Task ConsumeAsync(RubyGemArchive original, string version,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token) => await ConsumeAsync(original, version, false, execute, token);

    internal async Task<RubyArchiveFacts> InspectReleaseAsync(RubyGemArchive original,
        string version, Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        RequireOriginal(original);
        RubyArchiveFacts facts = await new RubyNativeFactCollector(tools, execute)
            .ArchiveAsync(original.Path, token);
        string[] expected = ["LICENSE", "README.md", "lib/hcoona_release_smoke_ruby.rb",
            "lib/hcoona_release_smoke_ruby/version.rb"];
        if (facts.Gem.Name != "hcoona-release-smoke-ruby" || facts.Gem.Version != version ||
            facts.Gem.Platform != "ruby" || facts.Gem.RuntimeDependencies.Length != 0 ||
            facts.Extensions is not [] || facts.Repository != "https://github.com/hcoona/three" ||
            !facts.Gem.RequirePaths.SequenceEqual(["lib"]) ||
            !facts.Contents.Order(StringComparer.Ordinal).SequenceEqual(expected.Order(
                StringComparer.Ordinal)))
            throw new InvalidDataException("Unsupported native Ruby Release archive.");
        RequireOriginal(original);
        return facts;
    }

    internal Task ConsumeReleaseAsync(RubyGemArchive original, string version,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token) => ConsumeAsync(original, version, true, execute, token);

    private async Task ConsumeAsync(RubyGemArchive original, string version, bool qualified,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        RequireOriginal(original);
        string consumer = Directory.CreateDirectory(Path.Combine(state, "consumer")).FullName;
        var environment = new Dictionary<string, string?>(tools.Environment,
            StringComparer.Ordinal)
        {
            ["GEM_HOME"] = Path.Combine(consumer, "gems"),
            ["GEM_PATH"] = Path.Combine(consumer, "gems"),
            ["RUBYLIB"] = null,
            ["RUBYOPT"] = null,
        };
        await RunAsync(new(tools.Ruby, consumer,
            [gem, "install", "--norc", original.Path, "--local", "--no-document"],
            tools.DeadlineSeconds, environment, InheritEnvironment: false,
            CloseStandardInput: true), execute, token);
        await RunAsync(new(tools.Ruby, consumer,
            ["-e", "require 'hcoona_release_smoke_ruby'; " +
                "abort 'wrong installed identity' unless " +
                "HcoonaReleaseSmokeRuby.project_id == 'hcoona-release-smoke-ruby' " +
                "&& HcoonaReleaseSmokeRuby::VERSION == ARGV.fetch(0); " +
                (qualified ? "spec = Gem.loaded_specs.fetch('hcoona-release-smoke-ruby'); " +
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
                version, environment["GEM_HOME"]!],
            tools.DeadlineSeconds, environment, InheritEnvironment: false,
            CloseStandardInput: true), execute, token);
        RequireOriginal(original);
    }

    internal static void RequireOriginal(RubyGemArchive original)
    {
        if (!File.Exists(original.Path) || OwnedFile.Length(original.Path) != original.Length ||
            Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(original.Path))) !=
            original.Sha256)
            throw new InvalidDataException("Original gem changed between required checks.");
    }

    private static async Task RunAsync(NativeCommand command,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        NativeCommandResult result = await execute(command, token);
        token.ThrowIfCancellationRequested();
        if (!result.Succeeded || result.Error is not null)
            throw new IOException("Native gem command failed: " +
                $"{result.Termination}, exit {result.ExitCode}. " +
                $"{result.Error} {result.Stderr}");
    }
}
