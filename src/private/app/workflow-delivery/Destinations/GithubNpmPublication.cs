using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

internal static class GithubNpmPublication
{
    internal static NpmPublishSession Bind(PackageDestination prepared,
        string originals, string scratch, string toolchain, string credential,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute)
    {
        if (prepared.Subject.DryRun)
            throw new InvalidDataException("Unsupported live GitHub npm request.");
        _ = GithubNpmObservation.Package(prepared);
        LiveReleaseRef.RequireEligible(prepared.Subject);
        return new(prepared.Files[0], originals, scratch, toolchain, execute, ConfigureEnvironment,
            ["--registry", GithubNpmObservation.Registry, "--tag",
                "buddy-sha-" + prepared.Subject.Revision], 120,
            userConfig: "@hcoona:registry=https://npm.pkg.github.com\n"
                + "//npm.pkg.github.com/:_authToken=${GITHUB_TOKEN}\n",
            packageJson: "{\"name\":\"workflow-delivery-consumer\",\"private\":true}\n");

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

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(GithubNpmToolSelection))]
internal partial class NpmToolJson : JsonSerializerContext;

internal sealed partial class NpmPublishSession(ReleaseFile original, string originals,
    string scratch, string toolchain, Func<NativeCommand, CancellationToken,
        Task<NativeCommandResult>> execute, Func<Dictionary<string, string?>, string>
        configureEnvironment, string[] publishOptions, int deadlineSeconds,
    bool closeStandardInput = false, string userConfig = "", string? packageJson = null,
    (Version Node, Version Npm)? minimumVersions = null)
{
    private (Dictionary<string, string?> Environment, string Secret)? runtime;

    internal async Task RequireRuntimeAsync(CancellationToken cancel)
    {
        if (runtime is not null) return;
        var selected = await PrepareAsync(scratch, toolchain, configureEnvironment,
            userConfig, packageJson, cancel);
        foreach (var (executable, expected) in SelectedVersions(toolchain))
        {
            NativeCommandResult query = await execute(Command(executable, ["--version"], 20,
                selected.Environment), cancel);
            if (!query.Succeeded || query.Stdout.Trim() != expected)
                throw new InvalidDataException("Unavailable qualified native publisher toolchain.");
            if (minimumVersions is { } minimum && Version.Parse(expected.TrimStart('v')) <
                (executable == "npm" ? minimum.Npm : minimum.Node))
                throw new InvalidDataException("Selected npm tools cannot use trusted publishing.");
        }
        runtime = selected;
    }

    internal async Task<ReleaseWriteStep> PublishAsync(ReleaseFile file, CancellationToken cancel)
    {
        if (file != original || runtime is not { } selected)
            throw new InvalidDataException("Missing qualified npm publisher runtime or original.");
        string archive = OwnedPath.ResolveWithin(originals, file.Path,
            "Original npm directory must be absolute.", "Original npm path leaves its root.");
        OwnedFile.Verify(archive, file.Size, file.Sha256, "Transferred npm original differs.");
        cancel.ThrowIfCancellationRequested();
        NativeCommandResult actual = await execute(Command("npm",
            ["publish", archive, .. publishOptions, "--ignore-scripts", "--fetch-retries=0"],
            deadlineSeconds, selected.Environment), cancel);
        return ReleaseWriteStep.FromUpload(new(file.Output, Sanitize(actual, selected.Secret)));
    }

    private NativeCommand Command(string executable, string[] arguments, int seconds,
        Dictionary<string, string?> environment) => new(Path.Combine(toolchain, executable),
            scratch, arguments, seconds, environment, InheritEnvironment: false,
            CloseStandardInput: closeStandardInput);

    private static (string Executable, string Expected)[] SelectedVersions(string toolchain)
    {
        var selected = JsonSerializer.Deserialize(File.ReadAllText(
            Path.Combine(toolchain, "selected-tools.json")),
                NpmToolJson.Default.GithubNpmToolSelection)
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

    private NativeCommandResult Sanitize(NativeCommandResult result, string credential)
    {
        return result with
        {
            Stdout = CleanValue(result.Stdout),
            Stderr = CleanValue(result.Stderr),
            Error = result.Error is null ? null : CleanValue(result.Error)
        };

        string CleanValue(string value)
        {
            string sanitized = SignedUrl().Replace(value.Replace(credential, "[redacted]",
                StringComparison.Ordinal), "[url]");
            foreach (string directory in new[] { originals, scratch, toolchain })
                sanitized = sanitized.Replace(directory, "[path]", OwnedPath.Comparison);
            return sanitized.Length <= 4096 ? sanitized : sanitized[..4096] + " [truncated]";
        }
    }

    [GeneratedRegex(@"https?://[^\s\""'<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex SignedUrl();
}
