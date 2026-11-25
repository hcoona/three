using WorkflowDelivery.Destinations;

namespace WorkflowDelivery.Release;

internal static class RubyReleaseCli
{
    internal static int? Run(string[] args, TextWriter output,
        Func<RubyPreparationRequest, CancellationToken, Task<RubyPreparationResult>>?
            prepare = null,
        Func<PreparedRelease, RubySourceTools, RubyReadCredential?, CancellationToken,
            Task<RubyObservationResult>>? observe = null,
        Func<RubyPreparationResult, string, RubyPublisherTools,
            IReadOnlyDictionary<string, string?>, CancellationToken,
            Task<RubyPublishResult>>? publish = null)
    {
        if (args is ["release", "prepare-ruby", var requestPath])
        {
            RubyPreparationResult result = (prepare ?? RubyPreparation.PrepareAsync)(
                ReleaseCli.Read(requestPath, TransferJson.Default.RubyPreparationRequest),
                CancellationToken.None).GetAwaiter().GetResult();
            ReleaseCli.Write(output, result, TransferJson.Default.RubyPreparationResult);
            if (result.Errors.Length != 0 || result.Prepared.Files.Length == 0) return 1;
            RubyRelease.Validate(result);
            return 0;
        }
        if (args is ["release", "observe-ruby", var preparationPath, var originals,
                var toolsPath])
        {
            RubyPreparationResult preparation = Preparation(preparationPath);
            RubyRelease.Originals(preparation, originals);
            RubyObservationResult result = Observe(preparation.Prepared,
                ReleaseCli.Read(toolsPath, TransferJson.Default.RubySourceTools),
                CancellationToken.None).GetAwaiter().GetResult();
            RubyRelease.ValidateObservation(preparation.Prepared, result);
            ReleaseCli.Write(output, result, TransferJson.Default.RubyObservationResult);
            return result.Observation.Files.All(file => file.State is ReleaseFileState.Exact or
                ReleaseFileState.Absent) && result.Observation.Checks.All(check =>
                    check.Status == ReleaseCheckStatus.Passed) ? 0 : 1;
        }
        if (args is ["release", "describe-ruby", var describePreparation,
                var describeObservation])
        {
            PreparedRelease prepared = Preparation(describePreparation).Prepared;
            RubyObservationResult observed = Observation(describeObservation);
            RubyRelease.ValidateObservation(prepared, observed);
            string[] writes = RubyRelease.Missing(prepared, observed)
                ? [prepared.Files[0].Output] : [];
            ReleaseDryRun result = ReleaseCompletion.Describe(prepared, observed.Observation,
                writes);
            ReleaseCli.Write(output, result, TransferJson.Default.ReleaseDryRun);
            return result.Ready ? 0 : 1;
        }
        if (args is ["release", "publish-ruby", var publishPreparation,
                var publishOriginals, var initialPath, var readToolsPath,
                var publisherToolsPath])
        {
            RubyPreparationResult preparation = Preparation(publishPreparation);
            RubySourceTools readTools = ReleaseCli.Read(readToolsPath,
                TransferJson.Default.RubySourceTools);
            RubyPublisherTools publisherTools = ReleaseCli.Read(publisherToolsPath,
                TransferJson.Default.RubyPublisherTools);
            var platform = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (string name in new[] { "GITHUB_TOKEN", "GEM_HOST_API_KEY" })
                platform[name] = Environment.GetEnvironmentVariable(name);
            RubyPublicationResult result = RubyRelease.PublishAsync(preparation,
                publishOriginals, Observation(initialPath),
                (prepared, token) => Observe(prepared, readTools with
                {
                    Scratch = ReleaseCli.FreshScratch(readTools.Scratch,
                        "Ruby phase scratch parent must be absolute."),
                }, token),
                (prepared, directory, token) => (publish ?? RubyNativePublisher.PublishAsync)(
                    prepared, directory, publisherTools with
                    {
                        Scratch = ReleaseCli.FreshScratch(publisherTools.Scratch,
                            "Ruby phase scratch parent must be absolute."),
                    }, platform, token), CancellationToken.None).GetAwaiter().GetResult();
            ReleaseCli.Write(output, result, TransferJson.Default.RubyPublicationResult);
            // Separate final verification retains the actual invocation outcome.
            return result.Errors.Length == 0 ? 0 : 1;
        }
        if (args is ["release", "complete-ruby", var completePreparation,
                var initialObservation, var finalObservation, var publicationPath])
        {
            RubyCompletionResult result = RubyRelease.Complete(
                Preparation(completePreparation).Prepared, Observation(initialObservation),
                Observation(finalObservation), ReleaseCli.ReadOptional(publicationPath,
                    TransferJson.Default.RubyPublicationResult));
            ReleaseCli.Write(output, result, TransferJson.Default.RubyCompletionResult);
            return result.Outcome.State == ReleaseBusinessState.Satisfied ? 0 : 1;
        }
        return null;

        Task<RubyObservationResult> Observe(PreparedRelease prepared, RubySourceTools tools,
            CancellationToken token) => (observe ?? RubyDestinationObservation.ObserveAsync)(
                prepared, tools, prepared.Subject.Destination == "github-packages-ruby" ? new(
                    Environment.GetEnvironmentVariable("GITHUB_ACTOR") ?? "",
                    Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? "") : null, token);
    }

    // The caller's job-inputs receive operation validated this preparation for the job.
    private static RubyPreparationResult Preparation(string path) =>
        ReleaseCli.Read(path, TransferJson.Default.RubyPreparationResult);

    private static RubyObservationResult Observation(string path) =>
        ReleaseCli.Read(path, TransferJson.Default.RubyObservationResult);
}
