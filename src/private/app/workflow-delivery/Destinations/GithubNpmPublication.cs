using System.Text.Json;
using System.Text.RegularExpressions;
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
        if (prepared.Subject.DryRun)
            throw new InvalidDataException("Unsupported live GitHub npm request.");
        _ = GithubNpmObservation.Package(prepared);
        var (before, uploads, after, errors) = await GithubNpmRuntime.PublishCoreAsync(prepared,
            prepared.Files[0], originals, scratch, toolchain, execute,
            cancellation => observe(prepared, credential, cancellation), ConfigureEnvironment,
            ["--registry", GithubNpmObservation.Registry, "--tag",
                "buddy-sha-" + prepared.Subject.Revision], 120, token,
            userConfig: "@hcoona:registry=https://npm.pkg.github.com\n"
                + "//npm.pkg.github.com/:_authToken=${GITHUB_TOKEN}\n",
            packageJson: "{\"name\":\"workflow-delivery-consumer\",\"private\":true}\n");
        return new(prepared.Subject, before, uploads, after, errors);

        string ConfigureEnvironment(Dictionary<string, string?> environment)
        {
            if (credential.Length == 0 || credential.Any(c => c is '\r' or '\n' or '\0'))
                throw new InvalidDataException(
                    "Native npm requires fresh absolute runtime and tools.");
            environment["GITHUB_TOKEN"] = credential;
            return credential;
        }
    }
}

internal sealed record GithubNpmToolSelection(string Node, string Npm);

internal static partial class GithubNpmRuntime
{
    internal static async Task<(TObservation Before, ReleaseUpload[] Uploads,
        TObservation After, string[] Errors)> PublishCoreAsync<TObservation>(
        PreparedRelease prepared, ReleaseFile file, string originals, string scratch,
        string toolchain, Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        Func<CancellationToken, Task<TObservation>> observe,
        Func<Dictionary<string, string?>, string> configureEnvironment,
        string[] publishOptions, int deadlineSeconds, CancellationToken token,
        bool closeStandardInput = false, string userConfig = "", string? packageJson = null,
        (Version Node, Version Npm)? minimumVersions = null)
        where TObservation : IReleaseObservation
    {
        string archive = Path.Combine(originals, file.Path);
        if (await NpmPackageOperations.OriginalAsync(archive, token) != (file.Size, file.Sha256))
            throw new InvalidDataException("Publication original differs from preparation.");
        var (environment, secretToRedact) = await PrepareAsync(scratch, toolchain,
            configureEnvironment, userConfig, packageJson, token);
        foreach (var (executable, expected) in SelectedVersions(toolchain))
        {
            NativeCommandResult query = await execute(Command(executable, ["--version"], 20),
                token);
            if (!query.Succeeded || query.Stdout.Trim() != expected)
                throw new InvalidDataException("Unavailable qualified native publisher toolchain.");
            if (minimumVersions is { } minimum && Version.Parse(expected.TrimStart('v')) <
                (executable == "npm" ? minimum.Npm : minimum.Node))
                throw new InvalidDataException("Selected npm tools cannot use trusted publishing.");
        }
        TObservation before = await observe(token);
        ReleaseFileState state = ValidateObservation(prepared, file, before.Observation);
        if (state == ReleaseFileState.Exact)
            return (before, [], before, []);
        if (state != ReleaseFileState.Absent || before.Observation.Checks.Any(check =>
                check.Status != ReleaseCheckStatus.Passed))
            return (before, [], before,
                ["Current destination state does not permit native creation."]);
        // Recheck the actual original at the write boundary. The platform owns approval.
        if (await NpmPackageOperations.OriginalAsync(archive, token) != (file.Size, file.Sha256))
            throw new InvalidDataException("Publication original changed before upload.");
        NativeCommandResult actual = await execute(Command("npm",
            ["publish", archive, .. publishOptions, "--ignore-scripts", "--fetch-retries=0"],
            deadlineSeconds), token);
        ReleaseUpload[] uploads = [new(file.Output, Sanitize(actual, secretToRedact))];
        // A handled abnormal native outcome permits reads, never another upload.
        TObservation after = await observe(token);
        ValidateObservation(prepared, file, after.Observation);
        return (before, uploads, after, []);

        NativeCommand Command(string executable, string[] arguments, int seconds) =>
            new(Path.Combine(toolchain, executable), scratch, arguments, seconds,
                environment, InheritEnvironment: false, CloseStandardInput: closeStandardInput);
    }

    private static ReleaseFileState ValidateObservation(PreparedRelease prepared, ReleaseFile file,
        ReleaseObservation observation)
    {
        if (observation.Subject != prepared.Subject || observation.Files is not [var observed] ||
            observed.Output != file.Output || observed.Name != file.Name ||
            observed.Version != file.Version)
            throw new InvalidDataException("Mismatched native publication observation.");
        if (observed.State == ReleaseFileState.Exact &&
            (observed.Size != file.Size || observed.Sha256 != file.Sha256))
            throw new InvalidDataException(
                "Exact native publication observation differs from original.");
        return observed.State;
    }

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

    private static async Task<(Dictionary<string, string?> Environment, string SecretToRedact)>
        PrepareAsync(string scratch, string toolchain,
        Func<Dictionary<string, string?>, string> configureEnvironment, string userConfig,
        string? packageJson, CancellationToken token)
    {
        if (!Path.IsPathFullyQualified(scratch) || !Path.IsPathFullyQualified(toolchain) ||
            !Directory.Exists(scratch) || !Directory.Exists(toolchain) ||
            Directory.EnumerateFileSystemEntries(scratch).Any())
            throw new InvalidDataException("Native npm requires fresh absolute runtime and tools.");
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
        string secretToRedact = configureEnvironment(environment);
        foreach (string name in new[] { "home", "cache", "temp" })
            Directory.CreateDirectory(Path.Combine(scratch, name));
        if (packageJson is not null)
            await File.WriteAllTextAsync(Path.Combine(scratch, "package.json"), packageJson, token);
        await File.WriteAllTextAsync(Path.Combine(scratch, "user.npmrc"), userConfig, token);
        await File.WriteAllTextAsync(Path.Combine(scratch, "global.npmrc"), "", token);
        return (environment, secretToRedact);
    }

    internal static NativeCommandResult Sanitize(NativeCommandResult result, string credential) =>
        result with
        {
            Stdout = CleanValue(result.Stdout, credential),
            Stderr = CleanValue(result.Stderr, credential),
            Error = result.Error is null ? null : CleanValue(result.Error, credential)
        };

    private static string CleanValue(string value, string credential)
    {
        string sanitized = SignedUrl().Replace(value.Replace(credential, "[redacted]",
            StringComparison.Ordinal), "[url]");
        return sanitized.Length <= 4096 ? sanitized : sanitized[..4096] + " [truncated]";
    }

    [GeneratedRegex(@"https?://[^\s\""'<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex SignedUrl();
}
