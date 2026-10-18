using System.Text.Json;

namespace WorkflowDelivery.Release;

// The root mise.toml and mise.lock are the only tool and runtime version authority. Release
// preparation accepts whatever locked version the committed configuration selects; it keeps no
// version literal or floor of its own. Each failed condition names its tool and condition.
internal static class LockedToolSelection
{
    internal static string[] Query(string tool) =>
        ["ls", "--json", "--current", "--locked", tool];

    // Pure check of one `mise ls --json --current --locked <tool>` answer for the checkout.
    internal static string Selected(string tool, string checkout, string answer)
    {
        using JsonDocument json = JsonDocument.Parse(answer);
        if (json.RootElement.ValueKind != JsonValueKind.Array ||
            json.RootElement.GetArrayLength() != 1)
            throw new InvalidDataException("Native tool " + tool +
                " has no single locked selection in the committed configuration.");
        JsonElement value = json.RootElement[0];
        string selected = Text(value, "version", tool);
        if (!Version.TryParse(selected, out Version? parsed) || parsed.Build < 0)
            throw new InvalidDataException("Native tool " + tool + " locked version '" +
                selected + "' is not an exact release version.");
        if (!value.TryGetProperty("active", out JsonElement active) ||
            active.ValueKind != JsonValueKind.True)
            throw new InvalidDataException("Native tool " + tool + " locked version " +
                selected + " is not active (not installed in the prepared tool data).");
        string expected = Path.Combine(checkout, "mise.toml");
        JsonElement source = value.TryGetProperty("source", out JsonElement found) &&
            found.ValueKind == JsonValueKind.Object ? found : default;
        string type = source.ValueKind == JsonValueKind.Object ? Text(source, "type", tool) : "";
        string path = source.ValueKind == JsonValueKind.Object ? Text(source, "path", tool) : "";
        if (type != "mise.toml" || path != expected)
            throw new InvalidDataException("Native tool " + tool + " is selected by " +
                (type.Length == 0 ? "an unknown source" : type + " '" + path + "'") +
                ", not the committed root " + expected + ".");
        return selected;
    }

    // The actual native tool must report the selected locked version.
    internal static void RequireReadback(string tool, string selected, string actual)
    {
        if (actual != selected)
            throw new InvalidDataException("Actual native " + tool + " version '" + actual +
                "' differs from the committed locked selection " + selected + ".");
    }

    private static string Text(JsonElement value, string name, string tool) =>
        value.TryGetProperty(name, out JsonElement text) &&
        text.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(text.GetString())
            ? text.GetString()!
            : throw new InvalidDataException("Native tool " + tool +
                " selection omits its " + name + ".");
}
