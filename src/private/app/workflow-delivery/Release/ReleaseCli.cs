using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WorkflowDelivery.Destinations;

namespace WorkflowDelivery.Release;

internal static class ReleaseCli
{
    internal static int? Run(string[] args, TextWriter output,
        Func<NpmPreparationRequest, CancellationToken, Task<NpmPreparationResult>>? prepare = null,
        Func<PreparedRelease, CancellationToken, Task<NpmjsObservationResult>>? observe = null)
    {
        if (ReleaseJobInputs.Run(args) is { } jobStatus) return jobStatus;
        if (PythonReleaseCli.Run(args, output) is { } pythonStatus) return pythonStatus;
        if (NuGetReleaseCli.Run(args, output) is { } nugetStatus) return nugetStatus;
        if (RubyReleaseCli.Run(args, output) is { } rubyStatus) return rubyStatus;
        if (GithubReleaseCli.Run(args, output) is { } githubStatus) return githubStatus;
        if (BrowserStoreReleaseCli.Run(args, output) is { } storeStatus) return storeStatus;
        if (args is ["release", "prepare-npm", var requestPath])
        {
            NpmPreparationResult result = (prepare ?? NpmPreparation.PrepareAsync)(
                Read(requestPath, TransferJson.Default.NpmPreparationRequest),
                CancellationToken.None).GetAwaiter().GetResult();
            if (result.Prepared.Files.Length != 0) ReleaseCompletion.Validate(result.Prepared);
            Write(output, result, TransferJson.Default.NpmPreparationResult);
            return result.Errors.Length == 0 && result.Prepared.Files.Length != 0 &&
                ReleasePolicy.Qualified(result.Prepared.RequiredChecks, result.Prepared.Checks)
                ? 0 : 1;
        }
        if (args is ["release", "observe-npmjs", var preparationPath, var scratch])
        {
            PreparedRelease prepared = Read(preparationPath,
                TransferJson.Default.NpmPreparationResult).Prepared;
            NpmRelease.VerifyOriginals(prepared, scratch);
            NpmjsObservationResult result;
            if (observe is null)
            {
                using HttpClient client = RegistryHttpClient.Create();
                result = new NpmjsObservation(client).ObserveAsync(prepared, CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            else result = observe(prepared, CancellationToken.None).GetAwaiter().GetResult();
            Write(output, result, TransferJson.Default.NpmjsObservationResult);
            return NpmRelease.Proceeds(result.Observation) ? 0 : 1;
        }
        if (args is ["release", "publish-npmjs", var officialPreparedPath, var officialOriginals,
                var officialScratch, var officialToolchain])
        {
            PreparedRelease prepared = Read(officialPreparedPath,
                TransferJson.Default.NpmPreparationResult).Prepared;
            NpmRelease.VerifyOriginals(prepared, officialOriginals);
            using HttpClient client = RegistryHttpClient.Create();
            var reader = new NpmjsObservation(client);
            var platform = Environment.GetEnvironmentVariables().Cast<System.Collections
                .DictionaryEntry>().ToDictionary(entry => (string)entry.Key,
                    entry => (string?)entry.Value, StringComparer.Ordinal);
            NpmjsPublicationResult result = NpmjsPublication.PublishAsync(prepared,
                officialOriginals, officialScratch, officialToolchain, platform,
                Platform.NativeProcess.ExecuteAsync, reader.ObserveAsync, CancellationToken.None)
                .GetAwaiter().GetResult();
            Write(output, result, TransferJson.Default.NpmjsPublicationResult);
            // Handled upload failure is data. Required verification owns completion.
            return result.Errors.Length == 0 ? 0 : 1;
        }
        if (args is ["release", "complete-npmjs", var officialCompletePrepared,
                var officialInitial, var officialFinal, var officialPublished])
        {
            PreparedRelease prepared = Read(officialCompletePrepared,
                TransferJson.Default.NpmPreparationResult).Prepared;
            NpmjsPublicationResult? published = ReadOptional(officialPublished,
                TransferJson.Default.NpmjsPublicationResult);
            ReleaseOutcome result = NpmRelease.Complete(prepared, Read(officialInitial,
                    TransferJson.Default.NpmjsObservationResult).Observation,
                Read(officialFinal, TransferJson.Default.NpmjsObservationResult).Observation,
                published);
            Write(output, result, TransferJson.Default.ReleaseOutcome);
            return result.State == ReleaseBusinessState.Satisfied ? 0 : 1;
        }
        if (args is ["release", "describe-npmjs", var preparedPath, var observedPath])
        {
            PreparedRelease prepared = Read(preparedPath,
                TransferJson.Default.NpmPreparationResult).Prepared;
            ReleaseObservation observation = Read(observedPath,
                TransferJson.Default.NpmjsObservationResult).Observation;
            ReleaseDryRun result = NpmRelease.DescribeNpmjs(prepared, observation);
            Write(output, result, TransferJson.Default.ReleaseDryRun);
            return result.Ready ? 0 : 1;
        }
        if (args is ["release", "observe-github-npm", var observePreparedPath,
                var observeOriginals])
        {
            PreparedRelease prepared = Read(observePreparedPath,
                TransferJson.Default.NpmPreparationResult).Prepared;
            NpmRelease.VerifyOriginals(prepared, observeOriginals);
            using HttpClient client = RegistryHttpClient.Create();
            GithubNpmObservationResult result = new GithubNpmObservation(client).ObserveAsync(
                prepared, RequiredEnv("GITHUB_TOKEN",
                    "Current GitHub read/write credential is unavailable."),
                CancellationToken.None).GetAwaiter().GetResult();
            Write(output, result, TransferJson.Default.GithubNpmObservationResult);
            return NpmRelease.Proceeds(result.Observation) ? 0 : 1;
        }
        if (args is ["release", "publish-github-npm", var publishPreparedPath, var publishOriginals,
                var publishScratch, var publishToolchain])
        {
            PreparedRelease prepared = Read(publishPreparedPath,
                TransferJson.Default.NpmPreparationResult).Prepared;
            NpmRelease.VerifyOriginals(prepared, publishOriginals);
            using HttpClient client = RegistryHttpClient.Create();
            var reader = new GithubNpmObservation(client);
            GithubNpmPublicationResult result = GithubNpmPublication.PublishAsync(prepared,
                publishOriginals, publishScratch, publishToolchain, RequiredEnv("GITHUB_TOKEN",
                    "Current GitHub read/write credential is unavailable."),
                Platform.NativeProcess.ExecuteAsync, reader.ObserveAsync, CancellationToken.None)
                .GetAwaiter().GetResult();
            Write(output, result, TransferJson.Default.GithubNpmPublicationResult);
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
            ReleaseDryRun result = NpmRelease.DescribeGithubNpm(prepared, observed.Observation);
            Write(output, result, TransferJson.Default.ReleaseDryRun);
            return result.Ready ? 0 : 1;
        }
        if (args is ["release", "complete-github-npm", var completePreparedPath,
                var completeInitialPath, var completeObservedPath, var completePublishedPath])
        {
            PreparedRelease prepared = Read(completePreparedPath,
                TransferJson.Default.NpmPreparationResult).Prepared;
            GithubNpmPublicationResult? published = ReadOptional(completePublishedPath,
                TransferJson.Default.GithubNpmPublicationResult);
            ReleaseOutcome result = NpmRelease.Complete(prepared, Read(completeInitialPath,
                    TransferJson.Default.GithubNpmObservationResult).Observation,
                Read(completeObservedPath,
                    TransferJson.Default.GithubNpmObservationResult).Observation,
                published);
            Write(output, result, TransferJson.Default.ReleaseOutcome);
            return result.State == ReleaseBusinessState.Satisfied ? 0 : 1;
        }
        return null;
    }

    internal static T? ReadOptional<T>(string path, JsonTypeInfo<T> type) where T : class =>
        path == "-" ? null : Read(path, type);

    internal static void Write<T>(TextWriter output, T value, JsonTypeInfo<T> type) =>
        output.WriteLine(JsonSerializer.Serialize(value, type));

    internal static string RequiredEnv(string name, string message,
        Func<string, string?>? get = null) =>
        (get ?? Environment.GetEnvironmentVariable)(name) is { Length: > 0 } value
            ? value : throw new InvalidDataException(message);

    internal static string FreshScratch(string parent, string message) =>
        Path.IsPathFullyQualified(parent) && Directory.Exists(parent)
            ? Directory.CreateDirectory(Path.Combine(parent, Guid.NewGuid().ToString("N"))).FullName
            : throw new InvalidDataException(message);

    internal static T Read<T>(string path, JsonTypeInfo<T> type)
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
