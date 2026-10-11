using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using WorkflowDelivery.Destinations;
using WorkflowDelivery.Ecosystems;

namespace WorkflowDelivery.Release;

internal static class ReleaseCli
{
    internal static int? Run(string[] args, TextWriter output,
        Func<NpmPreparationRequest, CancellationToken, Task<NpmPreparationResult>>? prepare = null,
        Func<PreparedRelease, CancellationToken, Task<NpmjsObservationResult>>? observe = null)
    {
        if (args is ["release", "prepare-npm", var requestPath])
        {
            NpmPreparationResult result = (prepare ?? NpmPreparation.PrepareAsync)(
                Read(requestPath, TransferJson.Default.NpmPreparationRequest),
                CancellationToken.None).GetAwaiter().GetResult();
            if (result.Prepared.Files.Length != 0) ReleaseCompletion.Validate(result.Prepared);
            Write(result, TransferJson.Default.NpmPreparationResult);
            return result.Errors.Length == 0 && result.Prepared.Files.Length != 0 &&
                result.Prepared.RequiredChecks.All(id => result.Prepared.Checks.Any(check =>
                    check.Id == id && check.Status == ReleaseCheckStatus.Passed)) ? 0 : 1;
        }
        if (args is ["release", "observe-npmjs", var preparationPath, var scratch])
        {
            PreparedRelease prepared = Read(preparationPath,
                TransferJson.Default.NpmPreparationResult).Prepared;
            ReleaseCompletion.Validate(prepared);
            if (!Path.IsPathFullyQualified(scratch))
                throw new InvalidDataException("Original release directory must be absolute.");
            foreach (ReleaseFile file in prepared.Files)
            {
                (long size, string digest) = NpmPackageOperations.OriginalAsync(
                    Path.Combine(scratch, file.Path), 25_000_000, CancellationToken.None)
                    .GetAwaiter().GetResult();
                if (size != file.Size || digest != file.Sha256)
                    throw new InvalidDataException(
                        "Original release transfer differs from preparation.");
            }
            NpmjsObservationResult result;
            if (observe is null)
            {
                using HttpClient client = NpmjsObservation.CreateClient();
                result = new NpmjsObservation(client).ObserveAsync(prepared, CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            else result = observe(prepared, CancellationToken.None).GetAwaiter().GetResult();
            Write(result, TransferJson.Default.NpmjsObservationResult);
            return 0;
        }
        if (args is ["release", "describe-npmjs", var preparedPath, var observedPath])
        {
            PreparedRelease prepared = Read(preparedPath,
                TransferJson.Default.NpmPreparationResult).Prepared;
            ReleaseObservation observation = Read(observedPath,
                TransferJson.Default.NpmjsObservationResult).Observation;
            if (prepared.Subject.Destination != "npmjs" || prepared.Files is not
                [{ Kind: "npm-tarball" }])
                throw new InvalidDataException("Unsupported npmjs hypothetical operation set.");
            ReleaseDryRun result = ReleaseCompletion.Describe(prepared, observation,
                observation.Files.Where(file => file.State == ReleaseFileState.Absent)
                    .Select(file => file.Output).ToArray());
            Write(result, TransferJson.Default.ReleaseDryRun);
            return result.Ready ? 0 : 1;
        }
        return null;

        void Write<T>(T value, JsonTypeInfo<T> type) =>
            output.WriteLine(JsonSerializer.Serialize(value, type));
    }

    private static T Read<T>(string path, JsonTypeInfo<T> type)
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
