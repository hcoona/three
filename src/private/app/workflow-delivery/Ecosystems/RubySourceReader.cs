using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.Ecosystems;

internal sealed record RubySourceTools(string Ruby, string Helper, string Scratch);
internal sealed record RubyReadCredential(string User, string Token);
internal sealed record RubySourceOriginal(string Path, string Name, string Version,
    string Platform, long Size, string Sha256);
internal enum RubySourceState { Absent, Present, Unknown }
internal sealed record RubySourceReadResult(RubySourceState State, RubySourceOriginal? Original,
    NativeCommandResult? Command, string[] Errors);

// Credentials end at the native read process. Only portable facts and a sanitized
// command outcome cross this boundary; no credential-bearing native diagnostic does.
internal static class RubySourceReader
{
    internal static Task<RubySourceReadResult> ReadAsync(string host, string version,
        RubySourceTools tools, RubyReadCredential? credential, CancellationToken token) =>
        ReadAsync(host, version, tools, credential, NativeProcess.ExecuteAsync, token);

    internal static async Task<RubySourceReadResult> ReadAsync(string host, string version,
        RubySourceTools tools, RubyReadCredential? credential,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        bool buddy = host == "https://rubygems.pkg.github.com/hcoona";
        if (!(buddy || host == "https://rubygems.org") ||
            string.IsNullOrWhiteSpace(version) ||
            new[] { tools.Ruby, tools.Helper, tools.Scratch }.Any(path =>
                !Path.IsPathFullyQualified(path)) || !Directory.Exists(tools.Scratch) ||
            Directory.EnumerateFileSystemEntries(tools.Scratch).Any())
            throw new InvalidDataException("Native Ruby reads require selected fresh tool state.");
        if (buddy && (credential is null || string.IsNullOrWhiteSpace(credential.User) ||
                string.IsNullOrWhiteSpace(credential.Token)))
            return new(RubySourceState.Unknown, null, null,
                ["Selected native Ruby read credential is unavailable."]);

        string scratch = Path.TrimEndingDirectorySeparator(Path.GetFullPath(tools.Scratch));
        string download = Directory.CreateDirectory(Path.Combine(scratch, "download")).FullName;
        string input = Path.Combine(scratch, "request.json");
        await File.WriteAllTextAsync(input, JsonSerializer.Serialize(new RubySourceRequest(host,
            "hcoona-release-smoke-ruby", version, download),
                RubyFactJson.Default.RubySourceRequest), token);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (string name in new[] { "SystemRoot", "WINDIR", "PATHEXT" })
            environment[name] = Environment.GetEnvironmentVariable(name);
        environment["PATH"] = string.Join(Path.PathSeparator,
            Path.GetDirectoryName(tools.Ruby), "/usr/bin", "/bin");
        foreach (string name in new[] { "HOME", "USERPROFILE", "XDG_CONFIG_HOME",
                     "XDG_CACHE_HOME", "XDG_DATA_HOME", "APPDATA", "LOCALAPPDATA",
                     "GEM_HOME", "GEM_PATH", "GEM_SPEC_CACHE", "TMPDIR", "TMP", "TEMP" })
            environment[name] = Directory.CreateDirectory(Path.Combine(scratch,
                name.ToLowerInvariant())).FullName;
        environment["LANG"] = "C.UTF-8";
        environment["LC_ALL"] = "C.UTF-8";
        if (buddy)
        {
            environment["RUBY_SOURCE_USER"] = credential!.User;
            environment["RUBY_SOURCE_TOKEN"] = credential.Token;
        }
        NativeCommandResult reached = await execute(new(tools.Ruby, scratch,
            [tools.Helper, input], 90, environment, InheritEnvironment: false,
            CloseStandardInput: true), token);
        NativeCommandResult command = reached with { Stdout = "", Stderr = "",
            Error = reached.Error is null ? null : "Native Ruby source process failed." };
        if (!reached.Succeeded || reached.Error is not null || token.IsCancellationRequested)
            return Unknown("Native Ruby source process did not complete.");
        try
        {
            if (Encoding.UTF8.GetByteCount(reached.Stdout) > 256 * 1024)
                return Unknown("Native Ruby source answer exceeds its result limit.");
            RubySourceAnswer value = JsonSerializer.Deserialize(reached.Stdout,
                RubyFactJson.Default.RubySourceAnswer)
                ?? throw new JsonException("Missing native Ruby source answer.");
            switch (value.State)
            {
                case "absent":
                    return new(RubySourceState.Absent, null, command, []);
                case "present":
                    string path = value.Path!;
                    string digest = value.Sha256!;
                    long size = value.Size ?? 0;
                    if (value.Name != "hcoona-release-smoke-ruby" ||
                        value.Version != version ||
                        value.Platform != "ruby" ||
                        !Path.IsPathFullyQualified(path) ||
                        // The directory itself fails the regular-file check below.
                        !OwnedPath.Within(Path.GetFullPath(path), download) ||
                        size <= 0 || size > ReleasePolicy.ReadbackOriginalLimit ||
                        !File.Exists(path) ||
                        new FileInfo(path).Length != size ||
                        Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))) !=
                            digest)
                        return Unknown("Native Ruby original or identity is inconsistent.");
                    return new(RubySourceState.Present,
                        new(path, "hcoona-release-smoke-ruby", version, "ruby", size, digest),
                        command, []);
                default:
                    return Unknown("Native Ruby complete source state is unavailable.");
            }
        }
        catch (Exception error) when (error is JsonException or IOException or
            UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException or
            ArgumentException or FormatException or OverflowException)
        { return Unknown("Native Ruby source answer or original is unavailable."); }

        RubySourceReadResult Unknown(string error) => new(RubySourceState.Unknown, null,
            command, [error]);
    }
}
