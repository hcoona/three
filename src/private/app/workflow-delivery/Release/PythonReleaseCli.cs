using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;

namespace WorkflowDelivery.Release;

internal static class PythonReleaseCli
{
    internal static int? Run(string[] args, TextWriter output,
        Func<PythonPreparationRequest, CancellationToken, Task<PythonPreparationResult>>?
            prepare = null,
        Func<PreparedRelease, PythonIndexTools, CancellationToken,
            Task<PythonIndexObservationResult>>? observe = null,
        Func<PreparedRelease, string, string, PythonPublisherTools, CancellationToken,
            Task<PythonPublishResult>>? publish = null,
        Func<PreparedRelease, string, PythonPackageTools, CancellationToken,
            Task<PythonRemoteConsumerResult>>? consume = null)
    {
        if (args is ["release", "prepare-python-index-tools", var toolRequest])
        {
            PythonIndexToolResult result = PythonIndexToolPreparation.PrepareAsync(
                ReleaseCli.Read(toolRequest, TransferJson.Default.PythonIndexToolRequest),
                CancellationToken.None).GetAwaiter().GetResult();
            Write(result, TransferJson.Default.PythonIndexToolResult);
            return result.Errors.Length == 0 ? 0 : 1;
        }
        if (args is ["release", "prepare-python", var requestPath])
        {
            PythonPreparationResult result = (prepare ?? PythonPreparation.PrepareAsync)(
                ReleaseCli.Read(requestPath, TransferJson.Default.PythonPreparationRequest),
                CancellationToken.None).GetAwaiter().GetResult();
            Write(result, TransferJson.Default.PythonPreparationResult);
            if (result.Errors.Length != 0 || result.Prepared.Files.Length == 0) return 1;
            PythonRelease.Validate(result);
            return 0;
        }
        if (args is ["release", "observe-python", var preparationPath, var originals,
                var indexToolsPath])
        {
            PythonPreparationResult preparation = Preparation(preparationPath);
            PythonRelease.Originals(preparation, originals);
            PythonIndexObservationResult result = Observe(preparation.Prepared,
                ReleaseCli.Read(indexToolsPath, TransferJson.Default.PythonIndexTools),
                CancellationToken.None).GetAwaiter().GetResult();
            PythonRelease.ValidateObservation(preparation.Prepared, result);
            Write(result, TransferJson.Default.PythonIndexObservationResult);
            return result.Observation.Files.All(file => file.State is ReleaseFileState.Exact or
                ReleaseFileState.Absent) && result.Observation.Checks.All(check =>
                    check.Status == ReleaseCheckStatus.Passed) ? 0 : 1;
        }
        if (args is ["release", "describe-python", var describePreparation,
                var describeObservation])
        {
            PreparedRelease prepared = Preparation(describePreparation).Prepared;
            PythonIndexObservationResult observed = Observation(describeObservation);
            PythonRelease.ValidateObservation(prepared, observed);
            ReleaseDryRun result = ReleaseCompletion.Describe(prepared, observed.Observation,
                PythonRelease.Missing(prepared, observed));
            Write(result, TransferJson.Default.ReleaseDryRun);
            return result.Ready ? 0 : 1;
        }
        if (args is ["release", "publish-python", var publishPreparation,
                var publishOriginals, var initialPath, var publishIndexToolsPath,
                var publisherToolsPath])
        {
            PythonPreparationResult preparation = Preparation(publishPreparation);
            PythonIndexTools indexTools = ReleaseCli.Read(publishIndexToolsPath,
                TransferJson.Default.PythonIndexTools);
            PythonPublisherTools publisherTools = ReleaseCli.Read(publisherToolsPath,
                TransferJson.Default.PythonPublisherTools);
            PythonPublicationResult result = PythonRelease.PublishAsync(preparation,
                publishOriginals, Observation(initialPath),
                (prepared, token) => Observe(prepared, indexTools with
                {
                    Scratch = Fresh(indexTools.Scratch),
                }, token),
                (prepared, directory, selected, token) => (publish ?? Publish)(prepared,
                    directory, selected, publisherTools with
                    {
                        Scratch = Fresh(publisherTools.Scratch),
                    }, token), CancellationToken.None).GetAwaiter().GetResult();
            Write(result, TransferJson.Default.PythonPublicationResult);
            // The required separate verification retains actual abnormal invocations.
            return result.Errors.Length == 0 ? 0 : 1;
        }
        if (args is ["release", "consume-python", var consumePreparation,
                var consumeOriginals, var kind, var packageToolsPath])
        {
            PythonPreparationResult preparation = Preparation(consumePreparation);
            PythonRelease.Originals(preparation, consumeOriginals);
            PythonPackageTools tools = ReleaseCli.Read(packageToolsPath,
                TransferJson.Default.PythonPackageTools);
            PythonRemoteConsumerResult result = (consume ?? PythonRemoteConsumer.RunAsync)(
                preparation.Prepared, kind, tools, CancellationToken.None)
                .GetAwaiter().GetResult();
            Write(result, TransferJson.Default.PythonRemoteConsumerResult);
            return result.Check.Status == ReleaseCheckStatus.Passed ? 0 : 1;
        }
        if (args is ["release", "complete-python", var completePreparation,
                var initialObservation, var finalObservation, var wheelPath, var sdistPath,
                var publicationPath])
        {
            var consumers = new List<PythonRemoteConsumerResult>();
            foreach (string path in new[] { wheelPath, sdistPath })
                if (path != "-") consumers.Add(ReleaseCli.Read(path,
                    TransferJson.Default.PythonRemoteConsumerResult));
            PythonCompletionResult result = PythonRelease.Complete(
                Preparation(completePreparation).Prepared, Observation(initialObservation),
                Observation(finalObservation), [.. consumers], publicationPath == "-" ? null :
                    ReleaseCli.Read(publicationPath, TransferJson.Default.PythonPublicationResult));
            Write(result, TransferJson.Default.PythonCompletionResult);
            return result.Outcome.State == ReleaseBusinessState.Satisfied ? 0 : 1;
        }
        return null;

        void Write<T>(T value, JsonTypeInfo<T> type) =>
            output.WriteLine(JsonSerializer.Serialize(value, type));

        async Task<PythonIndexObservationResult> Observe(PreparedRelease prepared,
            PythonIndexTools tools, CancellationToken token)
        {
            if (observe is not null) return await observe(prepared, tools, token);
            using HttpClient client = PythonIndexObservation.CreateClient();
            return await new PythonIndexObservation(client, tools).ObserveAsync(prepared, token);
        }
    }

    // The caller's job-inputs receive operation validated this preparation for the job.
    private static PythonPreparationResult Preparation(string path) =>
        ReleaseCli.Read(path, TransferJson.Default.PythonPreparationResult);

    private static PythonIndexObservationResult Observation(string path) =>
        ReleaseCli.Read(path, TransferJson.Default.PythonIndexObservationResult);

    private static Task<PythonPublishResult> Publish(PreparedRelease prepared, string originals,
        string selected, PythonPublisherTools tools, CancellationToken token)
    {
        var platform = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (string name in new[] { "GITHUB_ACTIONS", "ACTIONS_ID_TOKEN_REQUEST_URL",
                     "ACTIONS_ID_TOKEN_REQUEST_TOKEN", "SystemRoot" })
            platform[name] = Environment.GetEnvironmentVariable(name);
        return PythonNativePublisher.PublishAsync(prepared, originals, selected, tools,
            platform, Console.Error, token);
    }

    private static string Fresh(string parent)
    {
        if (!Path.IsPathFullyQualified(parent) || !Directory.Exists(parent))
            throw new InvalidDataException("Python phase scratch parent must be absolute.");
        return Directory.CreateDirectory(Path.Combine(parent, Guid.NewGuid().ToString("N")))
            .FullName;
    }
}
