using System.Text.Json;

namespace WorkflowDelivery.Ecosystems;

// Native MSBuild response projection shared by CI and Release.
internal static class DotNetPackageResults
{
    internal static async Task<JsonDocument> ResponseAsync(string path, CancellationToken token)
    {
        if (!File.Exists(path) || (File.GetAttributes(path) &
                (FileAttributes.Directory | FileAttributes.ReparsePoint)) != 0 ||
            new FileInfo(path).Length > 32 * 1024 * 1024)
            throw new InvalidDataException("Missing regular native package result file.");
        await using var file = File.OpenRead(path);
        return await JsonDocument.ParseAsync(file, cancellationToken: token);
    }

    internal static JsonElement Target(JsonElement root, string target)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("TargetResults", out JsonElement targets) ||
            targets.ValueKind != JsonValueKind.Object ||
            !targets.TryGetProperty(target, out JsonElement result) ||
            result.ValueKind != JsonValueKind.Object ||
            !result.TryGetProperty("Result", out JsonElement verdict) ||
            verdict.ValueKind != JsonValueKind.String ||
            verdict.GetString() != "Success")
            throw new InvalidDataException("Missing or failed native package target: " + target);
        return result;
    }

    internal static Dictionary<string, string> Properties(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("Properties", out JsonElement properties) ||
            properties.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Missing native package properties.");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in properties.EnumerateObject())
            if (property.Value.ValueKind != JsonValueKind.String ||
                !values.TryAdd(property.Name, property.Value.GetString()!))
                throw new InvalidDataException("Invalid or duplicate native package property.");
        return values;
    }
}
