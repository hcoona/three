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
        Func<string, string?>? environment = null,
        Func<HttpClient>? readHttp = null, Func<HttpClient>? writeHttp = null)
    {
        if (args is ["release", "prepare-browser-store", var requestPath])
        {
            BrowserStorePreparationResult result = (prepare ??
                BrowserStorePreparation.PrepareAsync)(ReleaseCli.Read(requestPath,
                    TransferJson.Default.BrowserStorePreparationRequest),
                CancellationToken.None).GetAwaiter().GetResult();
            ReleaseCli.Write(output, result, TransferJson.Default.BrowserStorePreparationResult);
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
            if (preparation.Target.Store == BrowserStorePreparation.ChromeWebStore &&
                !preparation.Prepared.Subject.DryRun)
            {
                using HttpClient client = readHttp?.Invoke() ?? RegistryHttpClient.Create();
                IBrowserStore adapter = store?.Invoke(preparation, false, client)
                    ?? Store(preparation, false, client, null);
                result = adapter.ObserveAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
            else result = BrowserStoreRelease.Unobserved(preparation);
            BrowserStoreRelease.ValidateObservation(preparation, result);
            ReleaseCli.Write(output, result, TransferJson.Default.StoreObservation);
            return result.State is StoreState.Conflict or StoreState.Unknown ? 1 : 0;
        }
        if (args is ["release", "describe-browser-store", var describePreparation,
                var describeObservation])
        {
            ReleaseDryRun result = BrowserStoreRelease.Describe(Preparation(describePreparation),
                Observation(describeObservation));
            ReleaseCli.Write(output, result, TransferJson.Default.ReleaseDryRun);
            return result.Ready ? 0 : 1;
        }
        if (args is ["release", "publish-browser-store", var publishPreparation,
                var publishOriginals, var initialPath])
        {
            BrowserStorePreparationResult preparation = Preparation(publishPreparation);
            StoreFile[] files = BrowserStoreRelease.OriginalsAsync(preparation,
                publishOriginals, CancellationToken.None).GetAwaiter().GetResult();
            using HttpClient client = readHttp?.Invoke() ?? RegistryHttpClient.Create();
            using HttpClient writeClient = writeHttp?.Invoke()
                ?? RegistryHttpClient.CreateNoRedirect();
            IBrowserStore adapter = store?.Invoke(preparation, true, client)
                ?? Store(preparation, true, client, writeClient);
            BrowserStorePublicationResult result = BrowserStoreRelease.PublishAsync(
                preparation, files, Observation(initialPath), adapter,
                CancellationToken.None).GetAwaiter().GetResult();
            ReleaseCli.Write(output, result, TransferJson.Default.BrowserStorePublicationResult);
            // The required separate verification retains abnormal submissions.
            return result.Errors.Length == 0 ? 0 : 1;
        }
        if (args is ["release", "complete-browser-store", var completePreparation,
                var initialObservation, var finalObservation, var publicationPath])
        {
            BrowserStoreCompletionResult result = BrowserStoreRelease.Complete(
                Preparation(completePreparation), Observation(initialObservation),
                ReleaseCli.ReadOptional(finalObservation, TransferJson.Default.StoreObservation),
                ReleaseCli.ReadOptional(publicationPath,
                    TransferJson.Default.BrowserStorePublicationResult));
            ReleaseCli.Write(output, result, TransferJson.Default.BrowserStoreCompletionResult);
            return result.Outcome.State == ReleaseBusinessState.Satisfied ? 0 : 1;
        }
        return null;

        IBrowserStore Store(BrowserStorePreparationResult preparation, bool write,
            HttpClient client, HttpClient? writeClient)
        {
            ReleaseSubject subject = preparation.Prepared.Subject;
            string version = preparation.Prepared.Files[0].Version;
            return preparation.Target.Store switch
            {
                BrowserStorePreparation.ChromeWebStore => new ChromeWebStore(client, subject,
                    preparation.Target, version, ReleaseCli.RequiredEnv("CWS_ACCESS_TOKEN",
                        "Missing store credential: CWS_ACCESS_TOKEN", environment),
                    writeClient: writeClient),
                BrowserStorePreparation.FirefoxAmo when write => new FirefoxAmo(client,
                    subject, preparation.Target, version, ReleaseCli.RequiredEnv("AMO_JWT_ISSUER",
                        "Missing store credential: AMO_JWT_ISSUER", environment),
                    ReleaseCli.RequiredEnv("AMO_JWT_SECRET",
                        "Missing store credential: AMO_JWT_SECRET", environment),
                    writeClient: writeClient),
                BrowserStorePreparation.EdgeAddons when write => new EdgeAddons(client,
                    subject, preparation.Target, version, ReleaseCli.RequiredEnv("EDGE_API_KEY",
                        "Missing store credential: EDGE_API_KEY", environment),
                    ReleaseCli.RequiredEnv("EDGE_CLIENT_ID",
                        "Missing store credential: EDGE_CLIENT_ID", environment),
                    writeClient: writeClient),
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
