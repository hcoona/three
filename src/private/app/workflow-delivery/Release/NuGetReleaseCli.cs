using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Release;

internal static class NuGetReleaseCli
{
    internal static int? Run(string[] args, TextWriter output,
        Func<NuGetPreparationRequest, CancellationToken, Task<NuGetPreparationResult>>?
            prepare = null,
        Func<PreparedRelease, string, CancellationToken, Task<GithubNuGetObservationResult>>?
            observe = null)
    {
        if (args is ["release", "prepare-nuget", var requestPath])
        {
            NuGetPreparationResult result = (prepare ?? NuGetPreparation.PrepareAsync)(
                ReleaseCli.Read(requestPath, TransferJson.Default.NuGetPreparationRequest),
                CancellationToken.None).GetAwaiter().GetResult();
            Write(result, TransferJson.Default.NuGetPreparationResult);
            if (result.Errors.Length != 0 || result.Prepared.Files.Length == 0) return 1;
            NuGetRelease.Validate(result);
            return 0;
        }
        if (args is ["release", "observe-github-nuget", var preparationPath, var originals])
        {
            NuGetPreparationResult preparation = Preparation(preparationPath);
            _ = NuGetRelease.Original(preparation, originals);
            using HttpClient client = GithubNuGetObservation.CreateClient();
            GithubNuGetObservationResult result = (observe ?? new GithubNuGetObservation(client)
                .ObserveAsync)(preparation.Prepared, Credential(), CancellationToken.None)
                .GetAwaiter().GetResult();
            NuGetRelease.ValidateObservation(preparation.Prepared, result);
            Write(result, TransferJson.Default.GithubNuGetObservationResult);
            return result.Observation.Files.All(file => file.State is ReleaseFileState.Exact or
                ReleaseFileState.Absent) && result.Observation.Checks.All(check =>
                    check.Status == ReleaseCheckStatus.Passed) ? 0 : 1;
        }
        if (args is ["release", "describe-github-nuget", var describePreparation,
                var describeObservation])
        {
            PreparedRelease prepared = Preparation(describePreparation).Prepared;
            GithubNuGetObservationResult observed = Observation(describeObservation);
            NuGetRelease.ValidateObservation(prepared, observed);
            ReleaseDryRun result = ReleaseCompletion.Describe(prepared, observed.Observation,
                observed.Observation.Files.Where(file => file.State == ReleaseFileState.Absent)
                    .Select(file => file.Output).ToArray());
            Write(result, TransferJson.Default.ReleaseDryRun);
            return result.Ready ? 0 : 1;
        }
        if (args is ["release", "publish-github-nuget", var publishPreparation,
                var publishOriginals])
        {
            NuGetPreparationResult preparation = Preparation(publishPreparation);
            using HttpClient client = GithubNuGetObservation.CreateClient();
            var reader = new GithubNuGetObservation(client);
            GithubNuGetPublicationResult result = NuGetRelease.PublishAsync(preparation,
                publishOriginals, Credential(), reader.ObserveAsync,
                (prepared, directory, endpoint, credential, token) =>
                    GithubNuGetPublisher.PushAsync(prepared, directory, endpoint, credential,
                        token), CancellationToken.None).GetAwaiter().GetResult();
            Write(result, TransferJson.Default.GithubNuGetPublicationResult);
            // Handled official upload failure is data; independent verification owns completion.
            return result.Errors.Length == 0 ? 0 : 1;
        }
        if (args is ["release", "consume-github-nuget", var consumePreparation,
                var consumeObservation, var consumeOriginals, var scratch])
        {
            NuGetPreparationResult preparation = Preparation(consumePreparation);
            GithubNuGetConsumerResult result = NuGetRelease.ConsumeAsync(preparation,
                Observation(consumeObservation), consumeOriginals, scratch, Credential(),
                NativeProcess.ExecuteAsync, CancellationToken.None).GetAwaiter().GetResult();
            Write(result, TransferJson.Default.GithubNuGetConsumerResult);
            return result.Check.Status == ReleaseCheckStatus.Passed ? 0 : 1;
        }
        if (args is ["release", "complete-github-nuget", var completePreparation,
                var initialObservation, var finalObservation, var consumerPath,
                var publisherPath])
        {
            GithubNuGetCompletionResult result = NuGetRelease.Complete(
                Preparation(completePreparation).Prepared, Observation(initialObservation),
                Observation(finalObservation), ReleaseCli.Read(consumerPath,
                    TransferJson.Default.GithubNuGetConsumerResult), publisherPath == "-" ? null
                    : ReleaseCli.Read(publisherPath,
                        TransferJson.Default.GithubNuGetPublicationResult));
            Write(result, TransferJson.Default.GithubNuGetCompletionResult);
            return result.Outcome.State == ReleaseBusinessState.Satisfied ? 0 : 1;
        }
        return null;

        void Write<T>(T value, JsonTypeInfo<T> type) =>
            output.WriteLine(JsonSerializer.Serialize(value, type));
    }

    // The caller's job-inputs receive operation validated this preparation for the job.
    private static NuGetPreparationResult Preparation(string path) =>
        ReleaseCli.Read(path, TransferJson.Default.NuGetPreparationResult);

    private static GithubNuGetObservationResult Observation(string path) =>
        ReleaseCli.Read(path, TransferJson.Default.GithubNuGetObservationResult);

    private static string Credential() => Environment.GetEnvironmentVariable("GITHUB_TOKEN")
        ?? throw new InvalidDataException("Current GitHub read/write credential is unavailable.");
}
