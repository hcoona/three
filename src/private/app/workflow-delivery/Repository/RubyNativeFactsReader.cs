using System.Text.Json;

namespace WorkflowDelivery.Repository;

internal sealed record RubyDependency(string Name, string Requirement);
internal sealed record RubyGemFacts(string Name, string Version, string Platform,
    string[] RequirePaths, string[] Files, RubyDependency[] RuntimeDependencies);
internal sealed record RubySourceGemFacts(string Gemspec, RubyGemFacts Gem);
internal sealed record RubyNativeSource(string Kind, string? Path = null,
    string? Gemspec = null, string? Directory = null, string? Uri = null, string? Revision = null);
internal sealed record RubyBundleSpecification(RubyGemFacts Gem, RubyNativeSource Source);
internal sealed record RubyBundleFacts(string Gemfile, string Lockfile, string BundlerVersion,
    string[] Gemfiles, string[] DirectDependencies, RubyBundleSpecification[] Specifications);
internal sealed record RubyArchiveFacts(string Archive, RubyGemFacts Gem, string[] Contents);

// RubyGems/Bundler own semantics. This reader binds and preserves their structured answers.
internal static class RubyNativeFactsReader
{
    internal static RubySourceGemFacts ParseSource(string text, string expectedGemspec) =>
        Read<RubySourceGemFacts>(text, value =>
            new(Bind(value, "gemspec", expectedGemspec), Gem(value)));

    internal static RubyArchiveFacts ParseArchive(string text, string expectedArchive) =>
        Read<RubyArchiveFacts>(text, value => new(Bind(value, "archive", expectedArchive),
            Gem(value), Strings(value, "contents")));

    internal static RubyBundleFacts ParseBundle(string text, string expectedGemfile,
        string expectedBundlerVersion) => Read<RubyBundleFacts>(text, value =>
        {
            string gemfile = Bind(value, "gemfile", expectedGemfile);
            string version = Text(value.GetProperty("bundler_version"));
            if (version != expectedBundlerVersion ||
                Text(value.GetProperty("locked_bundler_version")) != expectedBundlerVersion)
                throw new InvalidDataException("Native Bundler version differs from the request.");
            string[] gemfiles = Strings(value, "gemfiles").Select(Absolute).ToArray();
            if (!gemfiles.Contains(gemfile, PathComparison))
                throw new InvalidDataException("Native Bundler inputs omit the requested Gemfile.");
            RubyBundleSpecification[] specifications = value.GetProperty("specifications")
                .EnumerateArray().Select(specification => new RubyBundleSpecification(
                    Gem(specification), Source(specification.GetProperty("source")))).ToArray();
            return new(gemfile, Absolute(Text(value.GetProperty("lockfile"))), version,
                gemfiles, Strings(value, "direct_dependencies"), specifications);
        });

    private static RubyGemFacts Gem(JsonElement value) => new(
        Text(value.GetProperty("name")), Text(value.GetProperty("version")),
        Text(value.GetProperty("platform")), Strings(value, "require_paths"),
        Strings(value, "files"), value.GetProperty("runtime_dependencies").EnumerateArray()
            .Select(dependency => new RubyDependency(Text(dependency.GetProperty("name")),
                Text(dependency.GetProperty("requirement")))).ToArray());

    private static RubyNativeSource Source(JsonElement value)
    {
        string kind = Text(value.GetProperty("kind"));
        return kind switch
        {
            "path" or "local_git" => new(kind, Absolute(Text(value.GetProperty("path"))),
                Absolute(Text(value.GetProperty("gemspec"))),
                Absolute(Text(value.GetProperty("directory")))),
            "git" => new(kind, Uri: Text(value.GetProperty("uri")),
                Revision: Text(value.GetProperty("revision"))),
            "rubygems" or "metadata" => new(kind),
            _ => throw new InvalidDataException("Unsupported native Bundler source.")
        };
    }

    private static T Read<T>(string text, Func<JsonElement, T> parse)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            return parse(document.RootElement);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or
            KeyNotFoundException)
        {
            throw new InvalidDataException("Incomplete native Ruby fact response.", error);
        }
    }

    private static string Bind(JsonElement value, string field, string expected)
    {
        string actual = Absolute(Text(value.GetProperty(field)));
        if (!PathComparison.Equals(actual, Absolute(expected)))
            throw new InvalidDataException("Native Ruby response differs from the request.");
        return actual;
    }

    private static string Text(JsonElement value)
    {
        string? text = value.GetString();
        return string.IsNullOrWhiteSpace(text)
            ? throw new InvalidDataException("Missing native Ruby text.") : text;
    }

    private static string[] Strings(JsonElement value, string field) =>
        value.GetProperty(field).EnumerateArray().Select(Text).ToArray();

    private static string Absolute(string path) => Path.IsPathFullyQualified(path)
        ? Path.GetFullPath(path)
        : throw new InvalidDataException("Native Ruby path is not absolute.");

    private static StringComparer PathComparison => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
