using System.Text.RegularExpressions;
using System.Text.Json;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

internal sealed record GithubNpmPublicationResult(ReleaseSubject Subject,
    GithubNpmObservationResult Before, ReleaseUpload[] Uploads,
    GithubNpmObservationResult After, string[] Errors);

internal static partial class GithubNpmPublication
{
    internal static async Task<GithubNpmPublicationResult> PublishAsync(PreparedRelease prepared,
        string originals, string scratch, string toolchain, string credential,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        Func<PreparedRelease, string, CancellationToken, Task<GithubNpmObservationResult>> observe,
        CancellationToken token)
    {
        if (prepared.Subject.DryRun || prepared.Files is not
                [{ Name: GithubNpmObservation.Package, Kind: "npm-tarball" }] ||
            prepared.Subject.Channel != "buddy" ||
            prepared.Subject.Destination != "github-packages-npm")
            throw new InvalidDataException("Unsupported live GitHub npm request.");
        ReleaseFile file = prepared.Files[0];
        string archive = Path.Combine(originals, file.Path);
        (long size, string digest) = await NpmPackageOperations.OriginalAsync(archive, token);
        if (size != file.Size || digest != file.Sha256)
            throw new InvalidDataException("Publication original differs from preparation.");
        Dictionary<string, string?> environment = await GithubNpmRuntime.PrepareAsync(scratch,
            toolchain, credential, token);
        foreach ((string executable, string expected) in GithubNpmRuntime.SelectedVersions(
            toolchain))
        {
            NativeCommandResult query = await execute(Command(executable, ["--version"], 20),
                token);
            if (!query.Succeeded || query.Stdout.Trim() != expected)
                throw new InvalidDataException("Unavailable qualified native publisher toolchain.");
        }
        GithubNpmObservationResult before = await observe(prepared, credential, token);
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
            ["publish", archive, "--registry", GithubNpmObservation.Registry, "--tag",
                "buddy-sha-" + prepared.Subject.Revision, "--ignore-scripts", "--fetch-retries=0"],
            120), token);
        ReleaseUpload[] uploads = [new(file.Output, GithubNpmRuntime.Sanitize(actual, credential))];
        // A handled abnormal native outcome permits reads, never another upload.
        GithubNpmObservationResult after = await observe(prepared, credential, token);
        ValidateObservation(after);
        return new(prepared.Subject, before, uploads, after, []);

        NativeCommand Command(string executable, string[] arguments, int seconds) =>
            new(Path.Combine(toolchain, executable), scratch, arguments, seconds,
                environment, InheritEnvironment: false);

        void ValidateObservation(GithubNpmObservationResult result)
        {
            if (result.Observation.Subject != prepared.Subject || result.Observation.Files is not
                    [var observed] || observed.Output != file.Output ||
                        observed.Name != file.Name ||
                observed.Version != file.Version)
                throw new InvalidDataException("Mismatched native publication observation.");
            if (observed.State == ReleaseFileState.Exact &&
                (observed.Size != file.Size || observed.Sha256 != file.Sha256))
                throw new InvalidDataException(
                    "Exact native publication observation differs from original.");
        }
    }
}

internal sealed record GithubNpmToolSelection(string Node, string Npm);

internal static partial class GithubNpmRuntime
{
    internal static (string Executable, string Expected)[] SelectedVersions(string toolchain)
    {
        var selected = JsonSerializer.Deserialize(File.ReadAllText(
            Path.Combine(toolchain, "selected-tools.json")),
                TransferJson.Default.GithubNpmToolSelection)
            ?? throw new InvalidDataException("Missing selected native npm tools.");
        foreach (string version in new[] { selected.Node, selected.Npm })
            if (!Version.TryParse(version, out Version? parsed) || parsed.Build < 0 ||
                parsed.ToString() != version)
                throw new InvalidDataException("Selected native npm tools require exact versions.");
        return [("node", "v" + selected.Node), ("npm", selected.Npm)];
    }

    internal static async Task<Dictionary<string, string?>> PrepareAsync(string scratch,
        string toolchain, string credential, CancellationToken token)
    {
        if (!Path.IsPathFullyQualified(scratch) || !Path.IsPathFullyQualified(toolchain) ||
            !Directory.Exists(scratch) || !Directory.Exists(toolchain) ||
            Directory.EnumerateFileSystemEntries(scratch).Any() || credential.Length == 0 ||
            credential.Any(c => c is '\r' or '\n' or '\0'))
            throw new InvalidDataException("Native npm requires fresh absolute runtime and tools.");
        foreach (string name in new[] { "home", "cache", "temp" })
            Directory.CreateDirectory(Path.Combine(scratch, name));
        await File.WriteAllTextAsync(Path.Combine(scratch, "package.json"),
            "{\"name\":\"workflow-delivery-consumer\",\"private\":true}\n", token);
        await File.WriteAllTextAsync(Path.Combine(scratch, "user.npmrc"),
            "@hcoona:registry=https://npm.pkg.github.com\n"
                + "//npm.pkg.github.com/:_authToken=${GITHUB_TOKEN}\n", token);
        await File.WriteAllTextAsync(Path.Combine(scratch, "global.npmrc"), "", token);
        return new(StringComparer.Ordinal)
        {
            ["PATH"] = toolchain + Path.PathSeparator + "/usr/bin:/bin",
            ["HOME"] = Path.Combine(scratch, "home"),
            ["TMPDIR"] = Path.Combine(scratch, "temp"),
            ["GITHUB_TOKEN"] = credential,
            ["NPM_CONFIG_USERCONFIG"] = Path.Combine(scratch, "user.npmrc"),
            ["NPM_CONFIG_GLOBALCONFIG"] = Path.Combine(scratch, "global.npmrc"),
            ["NPM_CONFIG_CACHE"] = Path.Combine(scratch, "cache"),
            ["NPM_CONFIG_LOGS_MAX"] = "0",
        };
    }

    internal static NativeCommandResult Sanitize(NativeCommandResult result, string credential) =>
        result with { Stdout = CleanValue(result.Stdout, credential),
            Stderr = CleanValue(result.Stderr, credential),
            Error = result.Error is null ? null : CleanValue(result.Error, credential) };

    private static string CleanValue(string value, string credential)
    {
        string sanitized = SignedUrl().Replace(value.Replace(credential, "[redacted]",
            StringComparison.Ordinal), "[url]");
        return sanitized.Length <= 4096 ? sanitized : sanitized[..4096] + " [truncated]";
    }

    [GeneratedRegex(@"https?://[^\s\""'<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex SignedUrl();
}
