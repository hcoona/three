using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Ecosystems;

internal sealed record RubyPublisherTools(string Ruby, string Gem, string Scratch);
internal sealed record RubyPublishResult(ReleaseSubject Subject, ReleaseUpload Upload);

// The actual publisher job supplies the selected platform-issued key. RubyGems
// owns push and its native response/retry semantics (including one resend after a connection
// reset); the application never resends.
internal static class RubyNativePublisher
{
    internal static Task<RubyPublishResult> PublishAsync(RubyPreparationResult preparation,
        string originals, RubyPublisherTools tools,
        IReadOnlyDictionary<string, string?> platform, CancellationToken token) =>
        PublishAsync(preparation, originals, tools, platform, NativeProcess.ExecuteAsync, token);

    internal static async Task<RubyPublishResult> PublishAsync(RubyPreparationResult preparation,
        string originals, RubyPublisherTools tools,
        IReadOnlyDictionary<string, string?> platform,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        PreparedRelease prepared = preparation.Prepared;
        if (prepared.Subject.DryRun || prepared.Subject.Unit != "hcoona-release-smoke-ruby" ||
            prepared.Files is not [{ Kind: "ruby-gem", Name: "hcoona-release-smoke-ruby" }])
            throw new InvalidDataException("Native Ruby push requires a supported request.");
        bool buddy = prepared.Subject.Channel == "buddy" &&
            prepared.Subject.Destination == "github-packages-ruby";
        bool official = prepared.Subject.Channel == "official" &&
            prepared.Subject.Destination == "rubygems";
        if (!(buddy || official))
            throw new InvalidDataException("Unsupported native Ruby live channel or ref.");
        // The effect boundary repeats the request's live ref eligibility.
        ReleasePolicy.RequireEligible(prepared.Subject);
        if (new[] { originals, tools.Ruby, tools.Gem, tools.Scratch }.Any(path =>
                !Path.IsPathFullyQualified(path)) || !Directory.Exists(tools.Scratch) ||
            Directory.EnumerateFileSystemEntries(tools.Scratch).Any())
            throw new InvalidDataException("Native Ruby push requires prepared fresh tool state.");
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(originals));
        ReleaseFile original = prepared.Files[0];
        string path = Path.GetFullPath(Path.Combine(root, original.Path));
        if (path == root || !OwnedPath.Within(path, root))
            throw new InvalidDataException("Ruby original leaves its transfer root.");
        RubySmokePackageOperations.RequireOriginal(new(path, original.Size, original.Sha256));

        string keyName = buddy ? "GITHUB_TOKEN" : "GEM_HOST_API_KEY";
        if (!platform.TryGetValue(keyName, out string? key) || string.IsNullOrWhiteSpace(key))
            throw new InvalidDataException("Selected native Ruby publisher key is unavailable.");
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (string nameKey in new[] { "SystemRoot", "WINDIR", "PATHEXT" })
            environment[nameKey] = Environment.GetEnvironmentVariable(nameKey);
        environment["PATH"] = string.Join(Path.PathSeparator,
            Path.GetDirectoryName(tools.Ruby), "/usr/bin", "/bin");
        foreach (string nameKey in new[] { "HOME", "USERPROFILE", "XDG_CONFIG_HOME",
                     "XDG_CACHE_HOME", "XDG_DATA_HOME", "APPDATA", "LOCALAPPDATA",
                     "GEM_HOME", "GEM_PATH", "GEM_SPEC_CACHE", "TMPDIR", "TMP", "TEMP" })
            environment[nameKey] = Directory.CreateDirectory(Path.Combine(tools.Scratch,
                nameKey.ToLowerInvariant())).FullName;
        environment["LANG"] = "C.UTF-8";
        environment["LC_ALL"] = "C.UTF-8";
        environment["GEM_HOST_API_KEY"] = buddy ? "Bearer " + key : key;
        string host = buddy ? "https://rubygems.pkg.github.com/hcoona" : "https://rubygems.org";
        token.ThrowIfCancellationRequested();
        RubySmokePackageOperations.RequireOriginal(new(path, original.Size, original.Sha256));
        NativeCommandResult result = await execute(new(tools.Ruby, tools.Scratch,
            [tools.Gem, "push", "--norc", path, "--host", host], 180, environment,
            InheritEnvironment: false, CloseStandardInput: true), token);
        // Runner masking does not sanitize retained result files. Omit all native
        // diagnostics, including partial output after cancellation or timeout.
        result = result with { Stdout = "", Stderr = "", Error = result.Error is null ? null :
            "Native Ruby publisher process failed; effects remain uncertain." };
        return new(prepared.Subject, new(original.Output, result));
    }
}
