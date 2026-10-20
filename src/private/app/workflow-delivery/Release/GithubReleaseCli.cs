using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Release;

internal static class GithubReleaseCli
{
    internal static int? Run(string[] args, TextWriter output,
        Func<GithubReleasePreparationRequest, CancellationToken,
            Task<GithubReleasePreparationResult>>? prepare = null,
        Func<HttpClient>? client = null)
    {
        if (args is ["release", "prepare-github-release", var requestPath])
        {
            GithubReleasePreparationResult result = (prepare ??
                GithubReleasePreparation.PrepareAsync)(ReleaseCli.Read(requestPath,
                    TransferJson.Default.GithubReleasePreparationRequest),
                CancellationToken.None).GetAwaiter().GetResult();
            Write(result, TransferJson.Default.GithubReleasePreparationResult);
            if (result.Errors.Length != 0 || result.Prepared.Files.Length == 0) return 1;
            GithubRelease.Validate(result);
            return 0;
        }
        if (args is ["release", "observe-github-release", var preparationPath, var originals])
        {
            GithubReleasePreparationResult preparation = Preparation(preparationPath);
            GithubRelease.Originals(preparation, originals);
            GithubReleaseObservationResult result = Destination(destination =>
                destination.ObserveAsync(preparation.Prepared, CancellationToken.None));
            GithubRelease.ValidateObservation(preparation.Prepared, result);
            Write(result, TransferJson.Default.GithubReleaseObservationResult);
            return result.Observation.Files.All(file => file.State is ReleaseFileState.Exact or
                ReleaseFileState.Absent) && result.Observation.Checks.All(check =>
                    check.Status == ReleaseCheckStatus.Passed) ? 0 : 1;
        }
        if (args is ["release", "describe-github-release", var describePreparation,
                var describeObservation])
        {
            PreparedRelease prepared = Preparation(describePreparation).Prepared;
            GithubReleaseObservationResult observed = Observation(describeObservation);
            GithubRelease.ValidateObservation(prepared, observed);
            ReleaseDryRun result = ReleaseCompletion.Describe(prepared, observed.Observation,
                GithubRelease.Missing(prepared, observed));
            Write(result, TransferJson.Default.ReleaseDryRun);
            return result.Ready ? 0 : 1;
        }
        if (args is ["release", "publish-github-release", var publishPreparation,
                var publishOriginals, var initialPath])
        {
            GithubReleasePreparationResult preparation = Preparation(publishPreparation);
            GithubReleaseObservationResult initial = Observation(initialPath);
            GithubReleasePublicationResult result = Destination(destination =>
                GithubRelease.PublishAsync(preparation, publishOriginals, initial,
                    destination.ObserveAsync, destination.CreateAsync, destination.UploadAsync,
                    CancellationToken.None));
            Write(result, TransferJson.Default.GithubReleasePublicationResult);
            // The required separate verification retains actual abnormal invocations.
            return result.Errors.Length == 0 ? 0 : 1;
        }
        if (args is ["release", "complete-github-release", var completePreparation,
                var initialObservation, var finalObservation, var publicationPath])
        {
            GithubReleaseCompletionResult result = GithubRelease.Complete(
                Preparation(completePreparation).Prepared, Observation(initialObservation),
                Observation(finalObservation), publicationPath == "-" ? null :
                    ReleaseCli.Read(publicationPath,
                        TransferJson.Default.GithubReleasePublicationResult));
            Write(result, TransferJson.Default.GithubReleaseCompletionResult);
            return result.Outcome.State == ReleaseBusinessState.Satisfied ? 0 : 1;
        }
        return null;

        void Write<T>(T value, JsonTypeInfo<T> type) =>
            output.WriteLine(JsonSerializer.Serialize(value, type));

        T Destination<T>(Func<GithubReleaseDestination, Task<T>> action)
        {
            using HttpClient http = client?.Invoke() ?? RegistryHttpClient.Create();
            return action(new(http, Credential())).GetAwaiter().GetResult();
        }
    }

    // The caller's job-inputs receive operation validated this preparation for the job.
    private static GithubReleasePreparationResult Preparation(string path) =>
        ReleaseCli.Read(path, TransferJson.Default.GithubReleasePreparationResult);

    private static GithubReleaseObservationResult Observation(string path) =>
        ReleaseCli.Read(path, TransferJson.Default.GithubReleaseObservationResult);

    // The job's own GITHUB_TOKEN: contents:read to observe, contents:write only to publish.
    private static string Credential() =>
        Environment.GetEnvironmentVariable("GITHUB_TOKEN") is { Length: > 0 } token
            ? token : throw new InvalidDataException("GitHub Release requires GITHUB_TOKEN.");
}
