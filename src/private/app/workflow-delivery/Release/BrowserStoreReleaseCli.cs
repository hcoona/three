using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WorkflowDelivery.Destinations;

namespace WorkflowDelivery.Release;

internal static class BrowserStoreReleaseCli
{
    // The store factory receives the preparation and whether the publisher context holds a
    // write credential; observation and verification contexts hold read access at most.
    internal static int? Run(string[] args, TextWriter output,
        Func<BrowserStorePreparationRequest, CancellationToken,
            Task<BrowserStorePreparationResult>>? prepare = null,
        Func<BrowserStorePreparationResult, bool, HttpClient, IBrowserStore>? store = null,
        Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;
        store ??= Store;
        if (args is ["release", "prepare-browser-store", var requestPath])
        {
            BrowserStorePreparationResult result = (prepare ??
                BrowserStorePreparation.PrepareAsync)(ReleaseCli.Read(requestPath,
                    TransferJson.Default.BrowserStorePreparationRequest),
                CancellationToken.None).GetAwaiter().GetResult();
            Write(result, TransferJson.Default.BrowserStorePreparationResult);
            if (result.Errors.Length != 0 || result.Prepared.Files.Length == 0) return 1;
            BrowserStoreRelease.Validate(result);
            return 0;
        }
        if (args is ["release", "observe-browser-store", var observePreparation,
                var observeOriginals])
        {
            BrowserStorePreparationResult preparation = Preparation(observePreparation);
            _ = BrowserStoreRelease.OriginalsAsync(preparation, observeOriginals,
                CancellationToken.None).GetAwaiter().GetResult();
            StoreObservation result;
            if (preparation.Target.Store == BrowserStorePreparation.ChromeWebStore)
            {
                using HttpClient client = RegistryHttpClient.Create();
                result = store(preparation, false, client).ObserveAsync(CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            else result = BrowserStoreRelease.Unobserved(preparation);
            BrowserStoreRelease.ValidateObservation(preparation, result);
            Write(result, TransferJson.Default.StoreObservation);
            return result.State is StoreState.Conflict or StoreState.Unknown ? 1 : 0;
        }
        if (args is ["release", "describe-browser-store", var describePreparation,
                var describeObservation])
        {
            ReleaseDryRun result = BrowserStoreRelease.Describe(Preparation(describePreparation),
                Observation(describeObservation));
            Write(result, TransferJson.Default.ReleaseDryRun);
            return result.Ready ? 0 : 1;
        }
        if (args is ["release", "publish-browser-store", var publishPreparation,
                var publishOriginals, var initialPath])
        {
            BrowserStorePreparationResult preparation = Preparation(publishPreparation);
            StoreFile[] files = BrowserStoreRelease.OriginalsAsync(preparation,
                publishOriginals, CancellationToken.None).GetAwaiter().GetResult();
            using HttpClient client = RegistryHttpClient.Create();
            BrowserStorePublicationResult result = BrowserStoreRelease.PublishAsync(
                preparation, files, Observation(initialPath), store(preparation, true, client),
                CancellationToken.None).GetAwaiter().GetResult();
            Write(result, TransferJson.Default.BrowserStorePublicationResult);
            // The required separate verification retains abnormal submissions.
            return result.Errors.Length == 0 ? 0 : 1;
        }
        if (args is ["release", "complete-browser-store", var completePreparation,
                var initialObservation, var finalObservation, var publicationPath])
        {
            BrowserStoreCompletionResult result = BrowserStoreRelease.Complete(
                Preparation(completePreparation), Observation(initialObservation),
                finalObservation == "-" ? null : Observation(finalObservation),
                publicationPath == "-" ? null : ReleaseCli.Read(publicationPath,
                    TransferJson.Default.BrowserStorePublicationResult));
            Write(result, TransferJson.Default.BrowserStoreCompletionResult);
            return result.Outcome.State == ReleaseBusinessState.Satisfied ? 0 : 1;
        }
        return null;

        void Write<T>(T value, JsonTypeInfo<T> type) =>
            output.WriteLine(JsonSerializer.Serialize(value, type));

        string Secret(string name) => environment(name) is { Length: > 0 } value ? value :
            throw new InvalidDataException("Missing store credential: " + name);

        IBrowserStore Store(BrowserStorePreparationResult preparation, bool write,
            HttpClient client)
        {
            ReleaseSubject subject = preparation.Prepared.Subject;
            string version = preparation.Prepared.Files[0].Version;
            return preparation.Target.Store switch
            {
                BrowserStorePreparation.ChromeWebStore => new ChromeWebStore(client, subject,
                    preparation.Target, version, Secret("CWS_ACCESS_TOKEN")),
                BrowserStorePreparation.FirefoxAmo when write => new FirefoxAmo(client,
                    subject, preparation.Target, version, Secret("AMO_JWT_ISSUER"),
                    Secret("AMO_JWT_SECRET")),
                BrowserStorePreparation.EdgeAddons when write => new EdgeAddons(client,
                    subject, preparation.Target, version, Secret("EDGE_API_KEY"),
                    Secret("EDGE_CLIENT_ID")),
                _ => throw new InvalidDataException("Store has no read outside its publisher."),
            };
        }
    }

    // The caller's job-inputs receive operation validated this preparation for the job.
    private static BrowserStorePreparationResult Preparation(string path) =>
        ReleaseCli.Read(path, TransferJson.Default.BrowserStorePreparationResult);

    private static StoreObservation Observation(string path) =>
        ReleaseCli.Read(path, TransferJson.Default.StoreObservation);
}
