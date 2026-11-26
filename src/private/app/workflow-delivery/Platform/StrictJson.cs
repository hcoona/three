using System.Text;
using System.Text.Json;

namespace WorkflowDelivery.Platform;

internal static class StrictJson
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonDocumentOptions Options = new()
    { AllowDuplicateProperties = false };

    internal static JsonDocument Parse(ReadOnlyMemory<byte> utf8)
    {
        _ = Utf8.GetCharCount(utf8.Span);
        return JsonDocument.Parse(utf8, Options);
    }

    internal static JsonDocument Parse(string text, int maxDepth = 0) =>
        JsonDocument.Parse(text, Options with { MaxDepth = maxDepth });

    internal static JsonElement Property(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object &&
        value.TryGetProperty(name, out JsonElement property)
            ? property : throw new InvalidDataException($"Missing required JSON field: {name}.");

    internal static string Text(JsonElement value) => Text(value, allowEmpty: false);

    internal static string Text(JsonElement value, bool allowEmpty) =>
        value.ValueKind == JsonValueKind.String &&
        (allowEmpty || !string.IsNullOrWhiteSpace(value.GetString()))
            ? value.GetString()! : throw new InvalidDataException("Invalid JSON text value.");
}
