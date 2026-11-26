using WorkflowDelivery.Destinations;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Release;

internal static class NuGetReleaseCli
{
    internal static int? Run(string[] args, TextWriter output,
        Func<ReleasePreparationRequest, CancellationToken, Task<NuGetPreparationResult>>?
            prepare = null,
        Func<PreparedRelease, string, CancellationToken, Task<NuGetObservationResult>>?
            observe = null, TimeSpan? settleInterval = null)
    {
        if (args is ["release", "prepare-nuget", var requestPath])
        {
            NuGetPreparationResult result = (prepare ?? NuGetPreparation.PrepareAsync)(
                ReleaseCli.Read(requestPath, TransferJson.Default.ReleasePreparationRequest),
                CancellationToken.None).GetAwaiter().GetResult();
            ReleaseCli.Write(output, result, TransferJson.Default.NuGetPreparationResult);
            if (result.Errors.Length != 0 || result.Prepared.Files.Length == 0) return 1;
            result.Validate();
            return 0;
        }
        // The final observation names the publication result, if any; a returned upload can
        // precede native visibility, which the observation awaits within a bounded period.
        if (args is ["release", "observe-nuget", var preparationPath, var originals,
                var publicationPath])
        {
            NuGetPreparationResult preparation = Preparation(preparationPath);
            _ = NuGetRelease.Original(preparation, originals);
            using HttpClient client = RegistryHttpClient.Create();
            bool settle = ReleaseCli.ReadOptional(publicationPath,
                TransferJson.Default.NuGetPublicationResult)?.Invocation is
            { ReturnedSuccessfully: true };
            NuGetObservationResult result = NuGetRelease.SettleAsync(preparation.Prepared,
                observe ?? Observer(preparation.Prepared, client), ObserveCredential(
                    preparation.Prepared), settle ? NuGetRelease.SettleDeadline : TimeSpan.Zero,
                settleInterval ?? NuGetRelease.SettleInterval, CancellationToken.None)
                .GetAwaiter().GetResult();
            NuGetRelease.ValidateObservation(preparation.Prepared, result);
            ReleaseCli.Write(output, result, TransferJson.Default.NuGetObservationResult);
            return ReleaseCompletion.ObservationProceeds(result.Observation,
                allowAvailable: true) ? 0 : 1;
        }
        if (args is ["release", "describe-nuget", var describePreparation,
                var describeObservation])
        {
            PreparedRelease prepared = Preparation(describePreparation).Prepared;
            NuGetObservationResult observed = Observation(describeObservation);
            NuGetRelease.ValidateObservation(prepared, observed);
            ReleaseDryRun result = ReleaseCompletion.Describe(prepared, observed.Observation,
                ReleaseCompletion.MissingOutputs(observed.Observation));
            ReleaseCli.Write(output, result, TransferJson.Default.ReleaseDryRun);
            return result.Ready ? 0 : 1;
        }
        if (args is ["release", "publish-nuget", var publishPreparation,
                var publishOriginals])
        {
            NuGetPreparationResult preparation = Preparation(publishPreparation);
            using HttpClient client = RegistryHttpClient.Create();
            PreparedRelease selected = preparation.Prepared;
            Func<PreparedRelease, string, CancellationToken, Task<NuGetObservationResult>>
                reader = Observer(selected, client);
            string observation = ObserveCredential(selected);
            NuGetPublicationResult result = NuGetRelease.PublishAsync(preparation,
                publishOriginals, PublishCredential(selected),
                (prepared, _, token) => reader(prepared, observation, token),
                (prepared, directory, endpoint, credential, token) =>
                    NuGetPublisher.PushAsync(prepared, directory, endpoint, credential,
                        token), CancellationToken.None).GetAwaiter().GetResult();
            ReleaseCli.Write(output, result, TransferJson.Default.NuGetPublicationResult);
            // Handled official upload failure is data; independent verification owns completion.
            return result.Errors.Length == 0 ? 0 : 1;
        }
        if (args is ["release", "complete-nuget", var completePreparation,
                var initialObservation, var finalObservation, var publisherPath])
        {
            NuGetCompletionResult result = NuGetRelease.Complete(
                Preparation(completePreparation).Prepared, Observation(initialObservation),
                Observation(finalObservation), ReleaseCli.ReadOptional(publisherPath,
                    TransferJson.Default.NuGetPublicationResult));
            ReleaseCli.Write(output, result, TransferJson.Default.NuGetCompletionResult);
            return result.Outcome.State == ReleaseBusinessState.Satisfied ? 0 : 1;
        }
        return null;
    }

    // The caller's job-inputs receive operation validated this preparation for the job.
    private static NuGetPreparationResult Preparation(string path) =>
        ReleaseCli.Read(path, TransferJson.Default.NuGetPreparationResult);

    private static NuGetObservationResult Observation(string path) =>
        ReleaseCli.Read(path, TransferJson.Default.NuGetObservationResult);

    private static Func<PreparedRelease, string, CancellationToken, Task<NuGetObservationResult>>
        Observer(PreparedRelease prepared, HttpClient client) =>
        prepared.Subject.Destination == "nugetorg" ? new NuGetOrgObservation(client).ObserveAsync
            : new GithubNuGetObservation(client).ObserveAsync;

    // nuget.org state is anonymous; GitHub Packages state requires the job token.
    private static string ObserveCredential(PreparedRelease prepared) =>
        prepared.Subject.Destination == "nugetorg" ? "" : ReleaseCli.RequiredEnv("GITHUB_TOKEN",
            "Current destination credential is unavailable.");

    // The nuget.org key is the short-lived key exchanged by the official NuGet login action.
    private static string PublishCredential(PreparedRelease prepared) =>
        ReleaseCli.RequiredEnv(
            prepared.Subject.Destination == "nugetorg" ? "NUGET_API_KEY" : "GITHUB_TOKEN",
            "Current destination credential is unavailable.");
}
