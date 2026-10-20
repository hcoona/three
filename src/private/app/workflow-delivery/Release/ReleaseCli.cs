using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;

namespace WorkflowDelivery.Release;

internal static class ReleaseCli
{
    internal static int? Run(string[] args, TextWriter output,
        Func<NpmPreparationRequest, CancellationToken, Task<NpmPreparationResult>>? prepare = null,
        Func<PreparedRelease, CancellationToken, Task<NpmjsObservationResult>>? observe = null)
    {
        if (ReleaseJobInputs.Run(args) is { } jobStatus) return jobStatus;
        if (PythonReleaseCli.Run(args, output) is { } pythonStatus) return pythonStatus;
        if (NuGetReleaseCli.Run(args, output) is { } nugetStatus) return nugetStatus;
        if (RubyReleaseCli.Run(args, output) is { } rubyStatus) return rubyStatus;
        if (args is ["release", "prepare-npm", var requestPath])
        {
            NpmPreparationResult result = (prepare ?? NpmPreparation.PrepareAsync)(
                Read(requestPath, TransferJson.Default.NpmPreparationRequest),
                CancellationToken.None).GetAwaiter().GetResult();
            if (result.Prepared.Files.Length != 0) ReleaseCompletion.Validate(result.Prepared);
            Write(result, TransferJson.Default.NpmPreparationResult);
            return result.Errors.Length == 0 && result.Prepared.Files.Length != 0 &&
                result.Prepared.RequiredChecks.All(id => result.Prepared.Checks.Any(check =>
                    check.Id == id && check.Status == ReleaseCheckStatus.Passed)) ? 0 : 1;
        }
        if (args is ["release", "observe-npmjs", var preparationPath, var scratch])
        {
            PreparedRelease prepared = Read(preparationPath,
                TransferJson.Default.NpmPreparationResult).Prepared;
            VerifyOriginals(prepared, scratch);
            NpmjsObservationResult result;
            if (observe is null)
            {
                using HttpClient client = RegistryHttpClient.Create();
                result = new NpmjsObservation(client).ObserveAsync(prepared, CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            else result = observe(prepared, CancellationToken.None).GetAwaiter().GetResult();
            Write(result, TransferJson.Default.NpmjsObservationResult);
            return Proceeds(result.Observation) ? 0 : 1;
        }
        if (args is ["release", "publish-npmjs", var officialPreparedPath, var officialOriginals,
                var officialScratch, var officialToolchain])
        {
            PreparedRelease prepared = Read(officialPreparedPath,
                TransferJson.Default.NpmPreparationResult).Prepared;
            VerifyOriginals(prepared, officialOriginals);
            using HttpClient client = RegistryHttpClient.Create();
            var reader = new NpmjsObservation(client);
            var platform = Environment.GetEnvironmentVariables().Cast<System.Collections
                .DictionaryEntry>().ToDictionary(entry => (string)entry.Key,
                    entry => (string?)entry.Value, StringComparer.Ordinal);
            NpmjsPublicationResult result = NpmjsPublication.PublishAsync(prepared,
                officialOriginals, officialScratch, officialToolchain, platform,
                Platform.NativeProcess.ExecuteAsync, reader.ObserveAsync, CancellationToken.None)
                .GetAwaiter().GetResult();
            Write(result, TransferJson.Default.NpmjsPublicationResult);
            // Handled upload failure is data. Required verification owns completion.
            return result.Errors.Length == 0 ? 0 : 1;
        }
        if (args is ["release", "complete-npmjs", var officialCompletePrepared,
                var officialInitial, var officialFinal, var officialPublished])
        {
            PreparedRelease prepared = Read(officialCompletePrepared,
                TransferJson.Default.NpmPreparationResult).Prepared;
            NpmjsPublicationResult? published = officialPublished == "-" ? null
                : Read(officialPublished, TransferJson.Default.NpmjsPublicationResult);
            ReleaseOutcome result = Complete(prepared, Read(officialInitial,
                    TransferJson.Default.NpmjsObservationResult).Observation,
                Read(officialFinal, TransferJson.Default.NpmjsObservationResult).Observation,
                published?.Subject, published?.Uploads);
            Write(result, TransferJson.Default.ReleaseOutcome);
            return result.State == ReleaseBusinessState.Satisfied ? 0 : 1;
        }
        if (args is ["release", "describe-npmjs", var preparedPath, var observedPath])
        {
            PreparedRelease prepared = Read(preparedPath,
                TransferJson.Default.NpmPreparationResult).Prepared;
            ReleaseObservation observation = Read(observedPath,
                TransferJson.Default.NpmjsObservationResult).Observation;
            if (prepared.Subject.Destination != "npmjs" || prepared.Files is not
                [{ Kind: "npm-tarball" }])
                throw new InvalidDataException("Unsupported npmjs hypothetical operation set.");
            ReleaseDryRun result = ReleaseCompletion.Describe(prepared, observation,
                observation.Files.Where(file => file.State == ReleaseFileState.Absent)
                    .Select(file => file.Output).ToArray());
            Write(result, TransferJson.Default.ReleaseDryRun);
            return result.Ready ? 0 : 1;
        }
        if (args is ["release", "observe-github-npm", var observePreparedPath,
                var observeOriginals])
        {
            PreparedRelease prepared = Read(observePreparedPath,
                TransferJson.Default.NpmPreparationResult).Prepared;
            VerifyOriginals(prepared, observeOriginals);
            using HttpClient client = RegistryHttpClient.Create();
            GithubNpmObservationResult result = new GithubNpmObservation(client).ObserveAsync(
                prepared, Credential(), CancellationToken.None).GetAwaiter().GetResult();
            Write(result, TransferJson.Default.GithubNpmObservationResult);
            return Proceeds(result.Observation) ? 0 : 1;
        }
        if (args is ["release", "publish-github-npm", var publishPreparedPath, var publishOriginals,
                var publishScratch, var publishToolchain])
        {
            PreparedRelease prepared = Read(publishPreparedPath,
                TransferJson.Default.NpmPreparationResult).Prepared;
            VerifyOriginals(prepared, publishOriginals);
            using HttpClient client = RegistryHttpClient.Create();
            var reader = new GithubNpmObservation(client);
            GithubNpmPublicationResult result = GithubNpmPublication.PublishAsync(prepared,
                publishOriginals, publishScratch, publishToolchain, Credential(),
                Platform.NativeProcess.ExecuteAsync, reader.ObserveAsync, CancellationToken.None)
                .GetAwaiter().GetResult();
            Write(result, TransferJson.Default.GithubNpmPublicationResult);
            // Handled upload failure is data. Required verification owns completion.
            return result.Errors.Length == 0 ? 0 : 1;
        }
        if (args is ["release", "describe-github-npm", var describePreparation,
                var describeObservation])
        {
            PreparedRelease prepared = Read(describePreparation,
                TransferJson.Default.NpmPreparationResult).Prepared;
            GithubNpmObservationResult observed = Read(describeObservation,
                TransferJson.Default.GithubNpmObservationResult);
            if (prepared.Subject.Destination != "github-packages-npm")
                throw new InvalidDataException("Unsupported GitHub npm dry-run destination.");
            ReleaseDryRun result = ReleaseCompletion.Describe(prepared, observed.Observation,
                observed.Observation.Files.Where(file => file.State == ReleaseFileState.Absent)
                    .Select(file => file.Output).ToArray());
            Write(result, TransferJson.Default.ReleaseDryRun);
            return result.Ready ? 0 : 1;
        }
        if (args is ["release", "complete-github-npm", var completePreparedPath,
                var completeInitialPath, var completeObservedPath, var completePublishedPath])
        {
            PreparedRelease prepared = Read(completePreparedPath,
                TransferJson.Default.NpmPreparationResult).Prepared;
            GithubNpmPublicationResult? published = completePublishedPath == "-" ? null
                : Read(completePublishedPath, TransferJson.Default.GithubNpmPublicationResult);
            ReleaseOutcome result = Complete(prepared, Read(completeInitialPath,
                    TransferJson.Default.GithubNpmObservationResult).Observation,
                Read(completeObservedPath,
                    TransferJson.Default.GithubNpmObservationResult).Observation,
                published?.Subject, published?.Uploads);
            Write(result, TransferJson.Default.ReleaseOutcome);
            return result.State == ReleaseBusinessState.Satisfied ? 0 : 1;
        }
        return null;

        void Write<T>(T value, JsonTypeInfo<T> type) =>
            output.WriteLine(JsonSerializer.Serialize(value, type));
    }

    private static bool Proceeds(ReleaseObservation observation) =>
        observation.Files.All(file => file.State is ReleaseFileState.Exact or
            ReleaseFileState.Absent) && observation.Checks.All(check =>
                check.Status == ReleaseCheckStatus.Passed);

    // Completion is the registry observation of the exact coordinate and original bytes.
    private static ReleaseOutcome Complete(PreparedRelease prepared,
        ReleaseObservation initial, ReleaseObservation observed, ReleaseSubject? published,
        ReleaseUpload[]? uploads)
    {
        _ = ReleaseCompletion.Evaluate(prepared, initial, []);
        if (initial.RequiredChecks.Any(id => !initial.Checks.Any(
                check => check.Id == id && check.Status == ReleaseCheckStatus.Passed)) ||
            initial.Files.Any(file =>
                file.State is not (ReleaseFileState.Exact or ReleaseFileState.Absent)))
            throw new InvalidDataException("Initial npm observation could not proceed.");
        if (published is not null && published != prepared.Subject)
            throw new InvalidDataException("Mismatched publication result.");
        ReleaseOutcome result = ReleaseCompletion.Evaluate(prepared, observed, uploads ?? []);
        if (published is null && initial.Files.Any(file => file.State == ReleaseFileState.Absent))
            result = result with { State = ReleaseBusinessState.Unknown,
                Reasons = [.. result.Reasons,
                    "Required publication result is missing; effects remain unknown."] };
        return result;
    }

    private static string Credential() => Environment.GetEnvironmentVariable("GITHUB_TOKEN")
        ?? throw new InvalidDataException("Current GitHub read/write credential is unavailable.");

    // The job's receive operation validated the preparation; this checks transferred bytes.
    private static void VerifyOriginals(PreparedRelease prepared, string root)
    {
        if (!Path.IsPathFullyQualified(root))
            throw new InvalidDataException("Original release directory must be absolute.");
        foreach (ReleaseFile file in prepared.Files)
        {
            (long size, string digest) = NpmPackageOperations.OriginalAsync(Path.Combine(root,
                file.Path), CancellationToken.None).GetAwaiter().GetResult();
            if (size != file.Size || digest != file.Sha256)
                throw new InvalidDataException(
                    "Original release transfer differs from preparation.");
        }
    }

    internal static T Read<T>(string path, JsonTypeInfo<T> type)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path,
            new System.Text.UTF8Encoding(false, true)));
        Unique(document.RootElement);
        return document.RootElement.Deserialize(type)
            ?? throw new InvalidDataException("Missing required release input.");
    }

    private static void Unique(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in value.EnumerateArray()) Unique(item);
        if (value.ValueKind != JsonValueKind.Object) return;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in value.EnumerateObject())
        {
            if (!names.Add(property.Name))
                throw new InvalidDataException("Duplicate release input property.");
            Unique(property.Value);
        }
    }
}
