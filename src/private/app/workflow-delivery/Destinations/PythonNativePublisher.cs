using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

internal sealed record PythonPublisherTools(string Uv, string Scratch);
internal sealed record PythonPublishResult(ReleaseUpload Upload, bool MaskForwardingFailed,
    string DiagnosticLimitation);

// UV owns upload, OIDC and native retries. Raw commands, credentials and native
// diagnostic text remain transient; only actual process facts leave this boundary.
internal static class PythonNativePublisher
{
    private const string Package = "hcoona-release-smoke-python";
    private const string DiagnosticLimit = "Native publication diagnostics omitted: " +
        "they may contain credentials.";
    private static readonly string[] StateVariables = ["HOME", "USERPROFILE", "XDG_CONFIG_HOME",
        "XDG_CACHE_HOME", "UV_CACHE_DIR", "TMPDIR", "TMP", "TEMP"];

    internal static Task<PythonPublishResult> PublishAsync(PreparedRelease prepared,
        string originals, string output, PythonPublisherTools tools,
        IReadOnlyDictionary<string, string?> platform, TextWriter masking,
        CancellationToken token) => PublishAsync(prepared, originals, output, tools, platform,
            masking, NativeProcess.ExecuteAsync, token);

    internal static async Task<PythonPublishResult> PublishAsync(PreparedRelease prepared,
        string originals, string output, PythonPublisherTools tools,
        IReadOnlyDictionary<string, string?> platform, TextWriter masking,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        string upload = (prepared.Subject.Channel, prepared.Subject.Destination) switch
        {
            ("buddy", "testpypi") => "https://test.pypi.org/legacy/",
            ("official", "pypi") => "https://upload.pypi.org/legacy/",
            _ => throw new InvalidDataException("Unsupported native Python publication."),
        };
        if (prepared.Subject.DryRun || prepared.Subject.Unit != Package ||
            prepared.Files.Length != 2 ||
            prepared.Files.Count(file => file.Kind == "python-wheel") != 1 ||
            prepared.Files.Count(file => file.Kind == "python-sdist") != 1 ||
            prepared.Files.Any(file => file.Name != Package))
            throw new InvalidDataException("Unsupported Python publication.");
        // The effect boundary repeats the request's live ref eligibility.
        ReleasePolicy.RequireEligible(prepared.Subject);
        string root = Absolute(originals), scratch = Absolute(tools.Scratch);
        if (!Directory.Exists(root) || !Directory.Exists(scratch) ||
            Directory.EnumerateFileSystemEntries(scratch).Any() ||
            OwnedPath.Overlap(root, scratch))
            throw new InvalidDataException("Publication requires separate owned empty scratch.");
        string uv = Absolute(tools.Uv);
        ReleaseFile selected = prepared.Files.SingleOrDefault(file => file.Output == output)
            ?? throw new InvalidDataException("Unknown original Python output.");
        // Qualified identity comes from preparation; this boundary checks the actual
        // transferred original without another metadata or version implementation.
        foreach (ReleaseFile file in prepared.Files)
        {
            string path = Path.GetFullPath(Path.Combine(root, file.Path));
            if (!OwnedPath.Within(path, root) || path == root)
                throw new InvalidDataException("Original Python path leaves its root.");
            PythonSmokePackageOperations.RequireOriginal(new(file.Kind, path, file.Size,
                file.Sha256));
        }
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["GITHUB_ACTIONS"] = Required("GITHUB_ACTIONS"),
            ["ACTIONS_ID_TOKEN_REQUEST_URL"] = Required("ACTIONS_ID_TOKEN_REQUEST_URL"),
            ["ACTIONS_ID_TOKEN_REQUEST_TOKEN"] = Required("ACTIONS_ID_TOKEN_REQUEST_TOKEN"),
        };
        if (environment["GITHUB_ACTIONS"] != "true")
            throw new InvalidDataException("Native GitHub publication context is unavailable.");
        if (platform.TryGetValue("SystemRoot", out string? systemRoot))
            environment["SystemRoot"] = systemRoot;
        foreach (string name in StateVariables)
            environment[name] = Directory.CreateDirectory(Path.Combine(scratch,
                name.ToLowerInvariant())).FullName;
        token.ThrowIfCancellationRequested();
        NativeCommandResult native = await execute(new(uv, scratch,
            ["publish", "--no-config", "--publish-url", upload, "--trusted-publishing", "always",
                "--keyring-provider", "disabled", "--no-attestations", "--",
                Path.Combine(root, selected.Path)], 180, environment,
            InheritEnvironment: false, CloseStandardInput: true), token);
        bool failed = false;
        await ForwardAsync(native.Stdout);
        await ForwardAsync(native.Stderr);
        // Omit even partial/error output. Masking affects runner logs, not retained JSON.
        string? safeError = native.Error is null ? null : "Native failure details omitted.";
        var safe = new NativeCommandResult(native.Termination, native.ExitCode, "", "",
            native.DurationSeconds, safeError);
        return new(new(output, safe), failed, DiagnosticLimit);

        string Required(string name) => platform.TryGetValue(name, out string? value) &&
            !string.IsNullOrEmpty(value) ? value
                : throw new InvalidDataException("Required native platform context is missing.");

        async Task ForwardAsync(string text)
        {
            int start = 0, end;
            while ((end = text.IndexOf('\n', start)) >= 0)
            {
                string line = text[start..end];
                if (line.EndsWith('\r')) line = line[..^1];
                start = end + 1;
                if (!line.StartsWith("::add-mask::", StringComparison.Ordinal) ||
                    line.Length == "::add-mask::".Length || line.Contains('\r')) continue;
                try { await masking.WriteLineAsync(line); }
                catch (Exception error) when (error is IOException or ObjectDisposedException)
                { failed = true; }
            }
        }
    }

    private static string Absolute(string path) =>
        OwnedPath.Absolute(path, "Native publication paths must be absolute.");
}
