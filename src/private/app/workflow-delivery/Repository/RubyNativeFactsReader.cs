using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace WorkflowDelivery.Repository;

internal sealed record RubyDependency(string Name, string Requirement);
internal sealed record RubyGemFacts(string Name, string Version, string Platform,
    string[] RequirePaths, string[] Files, RubyDependency[] RuntimeDependencies);
internal sealed record RubySourceGemFacts(string Gemspec, RubyGemFacts Gem);
internal sealed record RubyNativeSource(string Kind, string? Path = null,
    string? Gemspec = null, string? Directory = null, string? Uri = null, string? Revision = null);
internal sealed record RubyLockedGem(string Name, string Version, string Platform,
    RubyDependency[] RuntimeDependencies);
internal sealed record RubyBundleSpecification(RubyLockedGem Gem, RubyNativeSource Source);
internal sealed record RubyBundleFacts(string Gemfile, string Lockfile, string BundlerVersion,
    string[] Gemfiles, string[] DirectDependencies, RubyBundleSpecification[] Specifications);
internal sealed record RubyArchiveFacts(string Archive, RubyGemFacts Gem, string[] Contents)
{
    internal string[]? Extensions { get; init; }
    internal string? Repository { get; init; }
}
internal sealed record RubyBundleContext(string Gemfile, string Lockfile,
    string BundlerVersion, string LockedBundlerVersion);
internal sealed record RubyVersionProjection(string Gemspec, string Raw, string Version,
    string Literal);

// RubyGems/Bundler own semantics. This reader binds and preserves their structured answers.
internal static class RubyNativeFactsReader
{
    internal static RubySourceGemFacts ParseSource(string text, string expectedGemspec)
    {
        RubySourceMessage value = Read(text, RubyFactJson.Default.RubySourceMessage);
        return new(Bind(value.Gemspec, expectedGemspec), Gem(value));
    }

    internal static RubyArchiveFacts ParseArchive(string text, string expectedArchive)
    {
        RubyArchiveMessage value = Read(text, RubyFactJson.Default.RubyArchiveMessage);
        return new(Bind(value.Archive, expectedArchive), Gem(value), Strings(value.Contents))
        {
            Extensions = value.Extensions is null ? null : Strings(value.Extensions),
            Repository = value.Metadata?.GetValueOrDefault("github_repo") is { } repository
                ? Text(repository) : null,
        };
    }

    internal static RubyBundleContext ParseContext(string text, string expectedGemfile,
        string expectedBundlerVersion)
    {
        RubyContextMessage value = Read(text, RubyFactJson.Default.RubyContextMessage);
        if (value.BundlerVersion != expectedBundlerVersion)
            throw new InvalidDataException("Native Bundler probe differs from the request.");
        return new(Bind(value.Gemfile, expectedGemfile), Absolute(Text(value.Lockfile)),
            Text(value.BundlerVersion), Text(value.LockedBundlerVersion));
    }

    internal static RubyVersionProjection ParseVersion(string text, string expectedGemspec,
        string expectedRaw)
    {
        RubyVersionMessage value = Read(text, RubyFactJson.Default.RubyVersionMessage);
        if (value.Raw != expectedRaw)
            throw new InvalidDataException("Native Ruby projection has a different version input.");
        return new(Bind(value.Gemspec, expectedGemspec), Text(value.Raw), Text(value.Version),
            Text(value.Literal));
    }

    internal static RubyBundleFacts ParseBundle(string text, string expectedGemfile,
        string expectedBundlerVersion)
    {
        RubyBundleMessage value = Read(text, RubyFactJson.Default.RubyBundleMessage);
        string gemfile = Bind(value.Gemfile, expectedGemfile);
        if (value.BundlerVersion != expectedBundlerVersion ||
            value.LockedBundlerVersion != expectedBundlerVersion)
            throw new InvalidDataException("Native Bundler version differs from the request.");
        string[] gemfiles = Strings(value.Gemfiles).Select(Absolute).ToArray();
        if (!gemfiles.Contains(gemfile, PathComparison))
            throw new InvalidDataException("Native Bundler inputs omit the requested Gemfile.");
        return new(gemfile, Absolute(Text(value.Lockfile)), Text(value.BundlerVersion),
            gemfiles, Strings(value.DirectDependencies), value.Specifications.Select(
                specification =>
                new RubyBundleSpecification(new(Text(specification.Name),
                    Text(specification.Version), Text(specification.Platform),
                    Dependencies(specification.RuntimeDependencies)),
                    Source(specification.Source))).ToArray());
    }

    private static RubyGemFacts Gem(RubyGemMessage value) => new(Text(value.Name), Text(
        value.Version),
        Text(value.Platform), Strings(value.RequirePaths), Strings(value.Files),
            Dependencies(value.RuntimeDependencies));

    private static RubyDependency[] Dependencies(RubyDependency[] values) => values
        .Select(dependency => new RubyDependency(Text(dependency.Name), Text(
            dependency.Requirement))).ToArray();

    private static RubyNativeSource Source(RubyNativeSource value) => value.Kind switch
    {
        "path" or "local_git" => new(value.Kind, Absolute(Text(value.Path)),
            Absolute(Text(value.Gemspec)), Absolute(Text(value.Directory))),
        "git" => new(value.Kind, Uri: Text(value.Uri), Revision: Text(value.Revision)),
        "rubygems" or "metadata" => new(value.Kind),
        _ => throw new InvalidDataException("Unsupported native Bundler source.")
    };

    private static T Read<T>(string text, JsonTypeInfo<T> type)
    {
        try
        {
            return JsonSerializer.Deserialize(text, type) ??
                throw new InvalidDataException("Missing native Ruby fact response.");
        }
        catch (JsonException error)
        { throw new InvalidDataException("Incomplete native Ruby fact response.", error); }
    }

    private static string Bind(string actual, string expected)
    {
        actual = Absolute(Text(actual));
        if (!PathComparison.Equals(actual, Absolute(expected)))
            throw new InvalidDataException("Native Ruby response differs from the request.");
        return actual;
    }
    private static string Text(string? value) => !string.IsNullOrWhiteSpace(value) ? value :
        throw new InvalidDataException("Missing native Ruby text.");
    private static string[] Strings(string[] values) => values.Select(value => Text(
        value)).ToArray();

    private static string Absolute(string path) => Path.IsPathFullyQualified(path)
        ? Path.GetFullPath(path)
        : throw new InvalidDataException("Native Ruby path is not absolute.");

    private static StringComparer PathComparison => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
