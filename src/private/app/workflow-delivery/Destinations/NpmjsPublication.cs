using System.Text.RegularExpressions;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

internal sealed record NpmjsPublicationResult(ReleaseSubject Subject,
    NpmjsObservationResult Before, ReleaseUpload[] Uploads, NpmjsObservationResult After,
    string[] Errors);

// npm trusted publishing owns the OIDC exchange and provenance; this boundary passes only
// the Actions identity context to the locked npm CLI and never handles a registry token.
internal static partial class NpmjsPublication
{
    internal const string Registry = "https://registry.npmjs.org/";
    // docs.npmjs.com trusted publishing requires npm CLI 11.5.1 and Node 22.14.0 or later.
    private static readonly Version MinimumNpm = new(11, 5, 1);
    private static readonly Version MinimumNode = new(22, 14, 0);
    private static readonly string[] Required = ["GITHUB_ACTIONS",
        "ACTIONS_ID_TOKEN_REQUEST_URL", "ACTIONS_ID_TOKEN_REQUEST_TOKEN"];
    private static readonly string[] Provenance = ["CI", "GITHUB_WORKFLOW_REF",
        "GITHUB_REPOSITORY", "GITHUB_SERVER_URL", "GITHUB_EVENT_NAME", "GITHUB_REPOSITORY_ID",
        "GITHUB_REPOSITORY_OWNER_ID", "GITHUB_REF", "GITHUB_SHA", "GITHUB_RUN_ID",
        "GITHUB_RUN_ATTEMPT", "RUNNER_ENVIRONMENT"];

    internal static async Task<NpmjsPublicationResult> PublishAsync(PreparedRelease prepared,
        string originals, string scratch, string toolchain,
        IReadOnlyDictionary<string, string?> platform,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        Func<PreparedRelease, CancellationToken, Task<NpmjsObservationResult>> observe,
        CancellationToken token)
    {
        NpmReleaseUnit unit = NpmReleaseUnits.Admit(prepared.Subject);
        if (prepared.Subject.DryRun || prepared.Subject.Channel != "official" ||
            prepared.Files is not [{ Kind: "npm-tarball" } file] ||
            file.Name != unit.OfficialName)
            throw new InvalidDataException("Unsupported live npmjs request.");
        string tag = DistTag(file.Version);
        string archive = Path.Combine(originals, file.Path);
        (long size, string digest) = await NpmPackageOperations.OriginalAsync(archive, token);
        if (size != file.Size || digest != file.Sha256)
            throw new InvalidDataException("Publication original differs from preparation.");
        if (!Path.IsPathFullyQualified(scratch) || !Path.IsPathFullyQualified(toolchain) ||
            !Directory.Exists(scratch) || !Directory.Exists(toolchain) ||
            Directory.EnumerateFileSystemEntries(scratch).Any())
            throw new InvalidDataException("Native npm requires fresh absolute runtime and tools.");
        string identity = Context("ACTIONS_ID_TOKEN_REQUEST_TOKEN");
        if (Context("GITHUB_ACTIONS") != "true")
            throw new InvalidDataException("Native GitHub publication context is unavailable.");
        foreach (string name in new[] { "home", "cache", "temp" })
            Directory.CreateDirectory(Path.Combine(scratch, name));
        // Empty configuration: no token exists, so npm can only use trusted publishing.
        await File.WriteAllTextAsync(Path.Combine(scratch, "user.npmrc"), "", token);
        await File.WriteAllTextAsync(Path.Combine(scratch, "global.npmrc"), "", token);
        var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PATH"] = toolchain + Path.PathSeparator + "/usr/bin:/bin",
            ["HOME"] = Path.Combine(scratch, "home"),
            ["TMPDIR"] = Path.Combine(scratch, "temp"),
            ["NPM_CONFIG_USERCONFIG"] = Path.Combine(scratch, "user.npmrc"),
            ["NPM_CONFIG_GLOBALCONFIG"] = Path.Combine(scratch, "global.npmrc"),
            ["NPM_CONFIG_CACHE"] = Path.Combine(scratch, "cache"),
            ["NPM_CONFIG_LOGS_MAX"] = "0",
        };
        foreach (string name in Required) environment[name] = Context(name);
        foreach (string name in Provenance)
            if (platform.TryGetValue(name, out string? value) && !string.IsNullOrEmpty(value))
                environment[name] = value;
        foreach ((string executable, string expected) in GithubNpmRuntime.SelectedVersions(
            toolchain))
        {
            NativeCommandResult query = await execute(Command(executable, ["--version"], 20),
                token);
            if (!query.Succeeded || query.Stdout.Trim() != expected)
                throw new InvalidDataException("Unavailable qualified native publisher toolchain.");
            if (Version.Parse(expected.TrimStart('v')) < (executable == "npm" ? MinimumNpm
                    : MinimumNode))
                throw new InvalidDataException("Selected npm tools cannot use trusted publishing.");
        }
        NpmjsObservationResult before = await observe(prepared, token);
        ValidateObservation(before);
        ReleaseFileState state = before.Observation.Files[0].State;
        if (state == ReleaseFileState.Exact)
            return new(prepared.Subject, before, [], before, []);
        if (state != ReleaseFileState.Absent || before.Observation.Checks.Any(check =>
                check.Status != ReleaseCheckStatus.Passed))
            return new(prepared.Subject, before, [], before,
                ["Current destination state does not permit native creation."]);
        // Recheck the actual original at the write boundary. The platform owns approval.
        (size, digest) = await NpmPackageOperations.OriginalAsync(archive, token);
        if (size != file.Size || digest != file.Sha256)
            throw new InvalidDataException("Publication original changed before upload.");
        NativeCommandResult actual = await execute(Command("npm",
            ["publish", archive, "--registry", Registry, "--access", "public", "--provenance",
                "--tag", tag, "--ignore-scripts", "--fetch-retries=0"], 180), token);
        ReleaseUpload[] uploads = [new(file.Output, GithubNpmRuntime.Sanitize(actual,
            identity))];
        // A handled abnormal native outcome permits reads, never another upload.
        NpmjsObservationResult after = await observe(prepared, token);
        ValidateObservation(after);
        return new(prepared.Subject, before, uploads, after, []);

        string Context(string name) => platform.TryGetValue(name, out string? value) &&
            !string.IsNullOrEmpty(value) ? value
                : throw new InvalidDataException("Required native platform context is missing.");

        NativeCommand Command(string executable, string[] arguments, int seconds) =>
            new(Path.Combine(toolchain, executable), scratch, arguments, seconds,
                environment, InheritEnvironment: false, CloseStandardInput: true);

        void ValidateObservation(NpmjsObservationResult result)
        {
            if (result.Observation.Subject != prepared.Subject || result.Observation.Files is not
                    [var observed] || observed.Output != file.Output ||
                observed.Name != file.Name || observed.Version != file.Version)
                throw new InvalidDataException("Mismatched native publication observation.");
            if (observed.State == ReleaseFileState.Exact &&
                (observed.Size != file.Size || observed.Sha256 != file.Sha256))
                throw new InvalidDataException(
                    "Exact native publication observation differs from original.");
        }
    }

    // A prerelease is published under its first prerelease identifier so that it never
    // moves "latest"; a stable version uses "latest".
    internal static string DistTag(string version)
    {
        int separator = version.IndexOfAny(['-', '+']);
        if (separator < 0) return "latest";
        if (version[separator] == '+')
            throw new InvalidDataException("Unsupported npm version for a distribution tag.");
        string tag = version[(separator + 1)..].Split('.', '+')[0].ToLowerInvariant();
        if (!Tag().IsMatch(tag) || tag == "latest")
            throw new InvalidDataException("Unsupported npm prerelease distribution tag.");
        return tag;
    }

    [GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial Regex Tag();
}
