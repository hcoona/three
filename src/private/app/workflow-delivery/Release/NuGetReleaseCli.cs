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
        Func<PreparedRelease, string, CancellationToken, Task<NuGetObservationResult>>?
            observe = null, TimeSpan? settleInterval = null)
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
        // The final observation names the publication result, if any; a returned upload can
        // precede native visibility, which the observation awaits within a bounded period.
        if (args is ["release", "observe-nuget", var preparationPath, var originals,
                var publicationPath])
        {
            NuGetPreparationResult preparation = Preparation(preparationPath);
            _ = NuGetRelease.Original(preparation, originals);
            using HttpClient client = RegistryHttpClient.Create();
            bool settle = publicationPath != "-" && ReleaseCli.Read(publicationPath,
                TransferJson.Default.NuGetPublicationResult).Invocation is
            { ReturnedSuccessfully: true };
            NuGetObservationResult result = NuGetRelease.SettleAsync(preparation.Prepared,
                observe ?? Observer(preparation.Prepared, client), ObserveCredential(
                    preparation.Prepared), settle ? NuGetRelease.SettleDeadline : TimeSpan.Zero,
                settleInterval ?? NuGetRelease.SettleInterval, CancellationToken.None)
                .GetAwaiter().GetResult();
            NuGetRelease.ValidateObservation(preparation.Prepared, result);
            Write(result, TransferJson.Default.NuGetObservationResult);
            return (NuGetRelease.Present(result.Observation) ||
                result.Observation.Files[0].State == ReleaseFileState.Absent) &&
                result.Observation.Checks.All(check =>
                    check.Status == ReleaseCheckStatus.Passed) ? 0 : 1;
        }
        if (args is ["release", "describe-nuget", var describePreparation,
                var describeObservation])
        {
            PreparedRelease prepared = Preparation(describePreparation).Prepared;
            NuGetObservationResult observed = Observation(describeObservation);
            NuGetRelease.ValidateObservation(prepared, observed);
            ReleaseDryRun result = ReleaseCompletion.Describe(prepared, observed.Observation,
                observed.Observation.Files.Where(file => file.State == ReleaseFileState.Absent)
                    .Select(file => file.Output).ToArray());
            Write(result, TransferJson.Default.ReleaseDryRun);
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
            Write(result, TransferJson.Default.NuGetPublicationResult);
            // Handled official upload failure is data; independent verification owns completion.
            return result.Errors.Length == 0 ? 0 : 1;
        }
        if (args is ["release", "complete-nuget", var completePreparation,
                var initialObservation, var finalObservation, var publisherPath])
        {
            NuGetCompletionResult result = NuGetRelease.Complete(
                Preparation(completePreparation).Prepared, Observation(initialObservation),
                Observation(finalObservation), publisherPath == "-" ? null
                    : ReleaseCli.Read(publisherPath,
                        TransferJson.Default.NuGetPublicationResult));
            Write(result, TransferJson.Default.NuGetCompletionResult);
            return result.Outcome.State == ReleaseBusinessState.Satisfied ? 0 : 1;
        }
        return null;

        void Write<T>(T value, JsonTypeInfo<T> type) =>
            output.WriteLine(JsonSerializer.Serialize(value, type));
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
        prepared.Subject.Destination == "nugetorg" ? "" : Variable("GITHUB_TOKEN");

    // The nuget.org key is the short-lived key exchanged by the official NuGet login action.
    private static string PublishCredential(PreparedRelease prepared) =>
        Variable(prepared.Subject.Destination == "nugetorg" ? "NUGET_API_KEY" : "GITHUB_TOKEN");

    private static string Variable(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value
            : throw new InvalidDataException("Current destination credential is unavailable.");
}
