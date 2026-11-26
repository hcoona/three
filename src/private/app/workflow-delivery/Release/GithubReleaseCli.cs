using WorkflowDelivery.Destinations;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Release;

internal static class GithubReleaseCli
{
    internal static int? Run(string[] args, TextWriter output,
        Func<ReleasePreparationRequest, CancellationToken,
            Task<GithubReleasePreparationResult>>? prepare = null,
        Func<HttpClient>? client = null)
    {
        if (args is ["release", "prepare-github-release", var requestPath])
        {
            GithubReleasePreparationResult result = (prepare ??
                GithubReleasePreparation.PrepareAsync)(ReleaseCli.Read(requestPath,
                    TransferJson.Default.ReleasePreparationRequest),
                CancellationToken.None).GetAwaiter().GetResult();
            ReleaseCli.Write(output, result, TransferJson.Default.GithubReleasePreparationResult);
            if (result.Errors.Length != 0 || result.Prepared.Files.Length == 0) return 1;
            result.Validate();
            return 0;
        }
        if (args is ["release", "observe-github-release", var preparationPath, var originals])
        {
            GithubReleasePreparationResult preparation = Preparation(preparationPath);
            GithubRelease.Originals(preparation, originals);
            GithubReleaseObservationResult result = Destination(destination =>
                destination.ObserveAsync(preparation.Prepared, CancellationToken.None));
            GithubRelease.ValidateObservation(preparation.Prepared, result);
            ReleaseCli.Write(output, result, TransferJson.Default.GithubReleaseObservationResult);
            return ReleaseCompletion.ObservationProceeds(result.Observation) ? 0 : 1;
        }
        if (args is ["release", "describe-github-release", var describePreparation,
                var describeObservation])
        {
            PreparedRelease prepared = Preparation(describePreparation).Prepared;
            GithubReleaseObservationResult observed = Observation(describeObservation);
            GithubRelease.ValidateObservation(prepared, observed);
            ReleaseDryRun result = ReleaseCompletion.Describe(prepared, observed.Observation,
                GithubRelease.Missing(prepared, observed));
            ReleaseCli.Write(output, result, TransferJson.Default.ReleaseDryRun);
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
            ReleaseCli.Write(output, result, TransferJson.Default.GithubReleasePublicationResult);
            // The required separate verification retains actual abnormal invocations.
            return result.Errors.Length == 0 ? 0 : 1;
        }
        if (args is ["release", "complete-github-release", var completePreparation,
                var initialObservation, var finalObservation, var publicationPath])
        {
            GithubReleaseCompletionResult result = GithubRelease.Complete(
                Preparation(completePreparation).Prepared, Observation(initialObservation),
                Observation(finalObservation), ReleaseCli.ReadOptional(publicationPath,
                    TransferJson.Default.GithubReleasePublicationResult));
            ReleaseCli.Write(output, result, TransferJson.Default.GithubReleaseCompletionResult);
            return result.Outcome.State == ReleaseBusinessState.Satisfied ? 0 : 1;
        }
        return null;

        T Destination<T>(Func<GithubReleaseDestination, Task<T>> action)
        {
            using HttpClient http = client?.Invoke() ?? RegistryHttpClient.Create();
            // The job's GITHUB_TOKEN has contents:read to observe, contents:write to publish.
            return action(new(http, ReleaseCli.RequiredEnv("GITHUB_TOKEN",
                "GitHub Release requires GITHUB_TOKEN."))).GetAwaiter().GetResult();
        }
    }

    // The caller's job-inputs receive operation validated this preparation for the job.
    private static GithubReleasePreparationResult Preparation(string path) =>
        ReleaseCli.Read(path, TransferJson.Default.GithubReleasePreparationResult);

    private static GithubReleaseObservationResult Observation(string path) =>
        ReleaseCli.Read(path, TransferJson.Default.GithubReleaseObservationResult);
}
