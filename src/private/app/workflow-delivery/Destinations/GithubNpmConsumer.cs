using System.Security.Cryptography;
using System.Text.Json;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Release;

namespace WorkflowDelivery.Destinations;

internal sealed record GithubNpmConsumerResult(ReleaseSubject Subject, ReleaseCheck Check,
    NativeCommandResult[] Commands, string[] Errors);

internal static class GithubNpmConsumer
{
    internal static async Task<GithubNpmConsumerResult> ConsumeAsync(PreparedRelease prepared,
        GithubNpmObservationResult observation, string originals, string scratch,
        string toolchain, string credential,
        Func<NativeCommand, CancellationToken, Task<NativeCommandResult>> execute,
        CancellationToken token)
    {
        ReleaseCompletion.Validate(prepared);
        if (prepared.Subject.Channel != "buddy" || prepared.Subject.Destination !=
                "github-packages-npm" || prepared.Files is not
                [{ Name: GithubNpmObservation.Package, Kind: "npm-tarball" }])
            throw new InvalidDataException("Unsupported native GitHub npm consumer.");
        ReleaseFile file = prepared.Files[0];
        if (observation.Observation.Subject != prepared.Subject ||
            observation.Observation.Files is not [{ State: ReleaseFileState.Exact } actual] ||
            actual.Output != file.Output || actual.Name != file.Name ||
            actual.Version != file.Version ||
            actual.Size != file.Size || actual.Sha256 != file.Sha256 ||
            observation.Observation.RequiredChecks.Any(id => !observation.Observation.Checks.Any(
                check => check.Id == id && check.Status == ReleaseCheckStatus.Passed)) ||
            observation.TarballUrlSha256 is null)
            throw new InvalidDataException("Native consumer requires exact destination originals.");
        string archive = Path.Combine(originals, file.Path);
        (long size, string digest) = await NpmPackageOperations.OriginalAsync(archive, 25_000_000,
            token);
        if (size != file.Size || digest != file.Sha256)
            throw new InvalidDataException("Consumer original differs from preparation.");
        Dictionary<string, string?> environment = await GithubNpmRuntime.PrepareAsync(scratch,
            toolchain, credential, token);
        var commands = new List<NativeCommandResult>();
        var errors = new List<string>();
        ReleaseCheckStatus status = ReleaseCheckStatus.Passed;
        try
        {
            foreach ((string executable, string expected) in GithubNpmRuntime.SelectedVersions(
                toolchain))
                if ((await Run(executable, ["--version"], 20)).Stdout.Trim() != expected)
                    throw new InvalidDataException("Unavailable native consumer toolchain.");
            await Run("npm", ["install", file.Name + "@" + file.Version,
                "--registry", GithubNpmObservation.Registry, "--ignore-scripts",
                "--fetch-retries=0", "--no-audit", "--no-fund", "--package-lock=true",
                "--save-exact"], 300);
            string lockText = await File.ReadAllTextAsync(
                Path.Combine(scratch, "package-lock.json"), token);
            ValidateLock(lockText, file, await File.ReadAllBytesAsync(archive, token));
            using (JsonDocument installed = JsonDocument.Parse(lockText))
            {
                string resolved = installed.RootElement.GetProperty("packages")
                    .GetProperty("node_modules/" + file.Name).GetProperty("resolved").GetString()!;
                string urlDigest = Convert.ToHexStringLower(SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(new Uri(resolved).AbsoluteUri)));
                if (urlDigest != observation.TarballUrlSha256)
                    throw new InvalidDataException("Native install resolved another tarball URL.");
            }
            NativeCommand import = AdoptedNpmPackage.Import(scratch);
            await Run(import.Executable, import.Arguments, import.DeadlineSeconds);
        }
        catch (OperationCanceledException)
        { status = ReleaseCheckStatus.Cancelled; errors.Add("Native consumer cancelled."); }
        catch (TimeoutException)
        { status = ReleaseCheckStatus.TimedOut; errors.Add("Native consumer timed out."); }
        catch (Exception failure) when (failure is InvalidDataException or IOException or
            JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            status = ReleaseCheckStatus.Failed;
            errors.Add("Native consumer or lock result failed.");
        }
        return new(prepared.Subject, new("native-registry-consumer", status), [.. commands],
            [.. errors]);

        async Task<NativeCommandResult> Run(string executable, string[] arguments, int seconds)
        {
            NativeCommandResult result = await execute(new(Path.Combine(toolchain, executable),
                scratch, arguments, seconds, environment, InheritEnvironment: false), token);
            commands.Add(GithubNpmRuntime.Sanitize(result, credential));
            if (result.Termination == NativeTermination.Cancelled)
                throw new OperationCanceledException(token);
            if (result.Termination == NativeTermination.TimedOut)
                throw new TimeoutException();
            if (!result.Succeeded) throw new InvalidDataException("Native consumer failed.");
            return result;
        }
    }

    // Consume npm's installed lock result; npm owns dependency resolution/integrity.
    internal static void ValidateLock(string text, ReleaseFile file, byte[] original)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        JsonElement root = document.RootElement;
        Unique(root);
        if (root.GetProperty("lockfileVersion").GetInt32() != 3)
            throw new InvalidDataException("Unsupported native npm lock result.");
        JsonElement package = root.GetProperty("packages").GetProperty("node_modules/" + file.Name);
        if (package.GetProperty("version").GetString() != file.Version ||
            !Uri.TryCreate(package.GetProperty("resolved").GetString(), UriKind.Absolute,
                out Uri? uri) || uri.Scheme != "https" || uri.Port != 443 ||
            uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 || uri.Host is not
                ("npm.pkg.github.com" or "pkg-npm.githubusercontent.com" or
                    "objects.githubusercontent.com" or
                    "github-registry-files.githubusercontent.com"))
            throw new InvalidDataException("Native installed package differs from selected feed.");
        string? integrity = package.GetProperty("integrity").GetString();
        // Native npm lockfiles also expose legacy SHA1 SRI. This compares npm's
        // result; SHA256 original-byte observation remains the release identity.
#pragma warning disable CA5350
        bool matches = integrity == "sha512-" + Convert.ToBase64String(SHA512.HashData(original)) ||
            integrity == "sha1-" + Convert.ToBase64String(SHA1.HashData(original));
#pragma warning restore CA5350
        if (!matches)
            throw new InvalidDataException("Native installed integrity differs from original.");
    }

    private static void Unique(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Expected native lock result object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty field in value.EnumerateObject())
        {
            if (!names.Add(field.Name))
                throw new InvalidDataException("Duplicate native lock field.");
            if (field.Value.ValueKind == JsonValueKind.Object) Unique(field.Value);
        }
    }
}
