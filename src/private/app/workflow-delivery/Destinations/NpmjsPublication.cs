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
        // Empty configuration: no token exists, so npm can only use trusted publishing.
        var (before, uploads, after, errors) = await GithubNpmRuntime.PublishCoreAsync(prepared,
            file, originals, scratch, toolchain, execute,
            cancellation => observe(prepared, cancellation), ConfigureEnvironment,
            ["--registry", Registry, "--access", "public", "--provenance", "--tag", tag],
            180, token, closeStandardInput: true, minimumVersions: (MinimumNode, MinimumNpm));
        return new(prepared.Subject, before, uploads, after, errors);

        string ConfigureEnvironment(Dictionary<string, string?> environment)
        {
            string identity = Context("ACTIONS_ID_TOKEN_REQUEST_TOKEN");
            if (Context("GITHUB_ACTIONS") != "true")
                throw new InvalidDataException("Native GitHub publication context is unavailable.");
            foreach (string name in Required) environment[name] = Context(name);
            foreach (string name in Provenance)
                if (platform.TryGetValue(name, out string? value) && !string.IsNullOrEmpty(value))
                    environment[name] = value;
            return identity;
        }

        string Context(string name) => platform.TryGetValue(name, out string? value) &&
            !string.IsNullOrEmpty(value) ? value
                : throw new InvalidDataException("Required native platform context is missing.");
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
