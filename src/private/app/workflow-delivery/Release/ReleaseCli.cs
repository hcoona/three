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
            ReleaseCompletion.Validate(prepared);
            if (!Path.IsPathFullyQualified(scratch))
                throw new InvalidDataException("Original release directory must be absolute.");
            foreach (ReleaseFile file in prepared.Files)
            {
                (long size, string digest) = NpmPackageOperations.OriginalAsync(
                    Path.Combine(scratch, file.Path), 25_000_000, CancellationToken.None)
                    .GetAwaiter().GetResult();
                if (size != file.Size || digest != file.Sha256)
                    throw new InvalidDataException(
                        "Original release transfer differs from preparation.");
            }
            NpmjsObservationResult result;
            if (observe is null)
            {
                using HttpClient client = NpmjsObservation.CreateClient();
                result = new NpmjsObservation(client).ObserveAsync(prepared, CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            else result = observe(prepared, CancellationToken.None).GetAwaiter().GetResult();
            Write(result, TransferJson.Default.NpmjsObservationResult);
            return 0;
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
            using HttpClient client = GithubNpmObservation.CreateClient();
            GithubNpmObservationResult result = new GithubNpmObservation(client).ObserveAsync(
                prepared, Credential(), CancellationToken.None).GetAwaiter().GetResult();
            Write(result, TransferJson.Default.GithubNpmObservationResult);
            return result.Observation.Files.All(file => file.State is ReleaseFileState.Exact or
                ReleaseFileState.Absent) && result.Observation.Checks.All(check =>
                    check.Status == ReleaseCheckStatus.Passed) ? 0 : 1;
        }
        if (args is ["release", "publish-github-npm", var publishPreparedPath, var publishOriginals,
                var publishScratch, var publishToolchain])
        {
            PreparedRelease prepared = Read(publishPreparedPath,
                TransferJson.Default.NpmPreparationResult).Prepared;
            VerifyOriginals(prepared, publishOriginals);
            using HttpClient client = GithubNpmObservation.CreateClient();
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
        if (args is ["release", "consume-github-npm", var consumePreparedPath,
                var consumeObservedPath,
                var consumeOriginals, var consumeScratch, var consumeToolchain])
        {
            PreparedRelease prepared = Read(consumePreparedPath,
                TransferJson.Default.NpmPreparationResult).Prepared;
            GithubNpmObservationResult observation = Read(consumeObservedPath,
                TransferJson.Default.GithubNpmObservationResult);
            VerifyOriginals(prepared, consumeOriginals);
            GithubNpmConsumerResult result = GithubNpmConsumer.ConsumeAsync(prepared, observation,
                consumeOriginals, consumeScratch, consumeToolchain, Credential(),
                Platform.NativeProcess.ExecuteAsync, CancellationToken.None)
                .GetAwaiter().GetResult();
            Write(result, TransferJson.Default.GithubNpmConsumerResult);
            return result.Check.Status == ReleaseCheckStatus.Passed ? 0 : 1;
        }
        if (args is ["release", "complete-github-npm", var completePreparedPath,
                var completeObservedPath,
                var completeConsumedPath, var completePublishedPath])
        {
            PreparedRelease prepared = Read(completePreparedPath,
                TransferJson.Default.NpmPreparationResult).Prepared;
            GithubNpmObservationResult observed = Read(completeObservedPath,
                TransferJson.Default.GithubNpmObservationResult);
            GithubNpmConsumerResult consumer = Read(completeConsumedPath,
                TransferJson.Default.GithubNpmConsumerResult);
            if (consumer.Subject != prepared.Subject ||
                consumer.Check.Id != "native-registry-consumer")
                throw new InvalidDataException("Mismatched required native consumer result.");
            GithubNpmPublicationResult? published = completePublishedPath == "-" ? null
                : Read(completePublishedPath, TransferJson.Default.GithubNpmPublicationResult);
            if (published is not null && published.Subject != prepared.Subject)
                throw new InvalidDataException("Mismatched publication result.");
            ReleaseObservation final = observed.Observation with
            {
                RequiredChecks = [.. observed.Observation.RequiredChecks, consumer.Check.Id],
                Checks = [.. observed.Observation.Checks, consumer.Check],
            };
            ReleaseOutcome result = ReleaseCompletion.Evaluate(prepared, final,
                published?.Uploads ?? []);
            Write(result, TransferJson.Default.ReleaseOutcome);
            return result.State == ReleaseBusinessState.Satisfied ? 0 : 1;
        }
        return null;

        void Write<T>(T value, JsonTypeInfo<T> type) =>
            output.WriteLine(JsonSerializer.Serialize(value, type));
    }

    private static string Credential() => Environment.GetEnvironmentVariable("GITHUB_TOKEN")
        ?? throw new InvalidDataException("Current GitHub read/write credential is unavailable.");

    private static void VerifyOriginals(PreparedRelease prepared, string root)
    {
        ReleaseCompletion.Validate(prepared);
        if (!Path.IsPathFullyQualified(root))
            throw new InvalidDataException("Original release directory must be absolute.");
        foreach (ReleaseFile file in prepared.Files)
        {
            (long size, string digest) = NpmPackageOperations.OriginalAsync(Path.Combine(root,
                file.Path), 25_000_000, CancellationToken.None).GetAwaiter().GetResult();
            if (size != file.Size || digest != file.Sha256)
                throw new InvalidDataException(
                    "Original release transfer differs from preparation.");
        }
    }

    private static T Read<T>(string path, JsonTypeInfo<T> type)
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
