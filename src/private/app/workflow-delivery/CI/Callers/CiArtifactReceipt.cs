using System.Text.Json;

namespace WorkflowDelivery.CI.Callers;

// One native Actions artifact receipt validator reused by every concrete CI receiver.
// Actions still owns lookup/download and native transport integrity.
internal static class CiArtifactReceipt
{
    internal static void Validate(string path, string name, long artifact, long run, long
        repository)
        => Validate(CiCallerTransfer.Read(path, TransferJson.Default.JsonElement), name,
            artifact, run, repository);

    internal static void Validate(JsonElement metadata, string name, long artifact, long run,
        long repository)
    {
        if (artifact <= 0 || run <= 0 || repository <= 0 ||
            metadata.ValueKind != JsonValueKind.Object ||
            !Number(metadata, "id", artifact) ||
            !metadata.TryGetProperty("name", out JsonElement nativeName) ||
            nativeName.ValueKind != JsonValueKind.String || nativeName.GetString() != name ||
            !metadata.TryGetProperty("expired", out JsonElement expired) ||
            expired.ValueKind != JsonValueKind.False ||
            !metadata.TryGetProperty("workflow_run", out JsonElement workflow) ||
            workflow.ValueKind != JsonValueKind.Object || !Number(workflow, "id", run) ||
            !Number(workflow, "repository_id", repository))
            throw new
                InvalidDataException("CI artifact has a different native producer or lifetime.");
    }

    private static bool Number(JsonElement value, string name, long expected) =>
        value.TryGetProperty(name, out JsonElement number) &&
        number.ValueKind == JsonValueKind.Number && number.TryGetInt64(out long actual) &&
        actual == expected;
}
