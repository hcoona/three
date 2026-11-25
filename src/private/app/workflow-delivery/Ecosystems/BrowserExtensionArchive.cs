using System.IO.Compression;
using System.Text.Json;

namespace WorkflowDelivery.Ecosystems;

// Content and clean-consumption checks of a WXT store zip. The stores own package
// processing and review; this establishes that each original is a loadable extension root
// whose manifest carries the selected NBGV projection and resolves every file it declares.
internal static class BrowserExtensionArchive
{
    private const int ManifestLimit = 1024 * 1024;

    internal static void ValidateExtension(string archive, int manifestVersion,
        BrowserExtensionExpectation expected)
    {
        using ZipArchive zip = ZipFile.OpenRead(archive);
        var (_, files) = Members(zip);
        ZipArchiveEntry entry = zip.GetEntry("manifest.json") ??
            throw new InvalidDataException("Extension zip has no root manifest.json.");
        if (entry.Length > ManifestLimit)
            throw new InvalidDataException("Extension manifest exceeds its size limit.");
        using Stream stream = entry.Open();
        using JsonDocument document = JsonDocument.Parse(stream);
        JsonElement manifest = document.RootElement;
        if (manifest.ValueKind != JsonValueKind.Object ||
            !manifest.TryGetProperty("manifest_version", out JsonElement kind) ||
            kind.ValueKind != JsonValueKind.Number || kind.GetInt32() != manifestVersion)
            throw new InvalidDataException("Unexpected extension manifest_version.");
        if (Text(manifest, "name") != expected.Name)
            throw new InvalidDataException("Unexpected extension manifest name.");
        if (Text(manifest, "version") != expected.Version)
            throw new InvalidDataException("Extension manifest version differs from NBGV.");
        if (manifest.TryGetProperty("version_name", out JsonElement label) &&
            (label.ValueKind != JsonValueKind.String || label.GetString() != expected.Label))
            throw new InvalidDataException("Extension version_name differs from NBGV.");
        foreach (string path in References(manifest))
            if (!files.Contains(path.TrimStart('/')))
                throw new InvalidDataException("Extension manifest references a missing file: "
                    + path);
    }

    internal static void ValidateMembers(string archive)
    {
        using ZipArchive zip = ZipFile.OpenRead(archive);
        _ = Members(zip);
    }

    // The sources zip is rooted at the workspace so a reviewer installs from its lockfile.
    internal static void ValidateSources(string archive, string product)
    {
        using ZipArchive zip = ZipFile.OpenRead(archive);
        var (all, files) = Members(zip);
        foreach (string required in new[] { "pnpm-lock.yaml", "pnpm-workspace.yaml",
            product + "/package.json", product + "/wxt.config.ts" })
            if (!files.Contains(required))
                throw new InvalidDataException("Source zip omits " + required + ".");
        if (all.Any(name => name.Split('/').Any(part => part is "node_modules" or ".output")))
            throw new InvalidDataException("Source zip contains build or dependency output.");
    }

    private static (HashSet<string> All, HashSet<string> Files) Members(ZipArchive zip)
    {
        var all = new HashSet<string>(StringComparer.Ordinal);
        var files = new HashSet<string>(StringComparer.Ordinal);
        var directories = new HashSet<string>(StringComparer.Ordinal);
        foreach (ZipArchiveEntry entry in zip.Entries)
        {
            string trimmed = entry.FullName.TrimEnd('/');
            string[] parts = trimmed.Split('/');
            if (entry.FullName.Contains('\\') || entry.FullName.StartsWith('/') ||
                parts.Any(part => part is "" or "." or "..") || !all.Add(entry.FullName))
                throw new InvalidDataException("Unsupported or duplicate zip member path.");
            for (int i = 1; i < parts.Length; i++)
                directories.Add(string.Join('/', parts[..i]));
            if (entry.FullName.EndsWith('/')) directories.Add(trimmed);
            else files.Add(entry.FullName);
        }
        if (files.Overlaps(directories))
            throw new InvalidDataException("Unsupported or duplicate zip member path.");
        return (all, files);
    }

    private static IEnumerable<string> References(JsonElement manifest)
    {
        if (manifest.TryGetProperty("icons", out JsonElement icons))
            foreach (string path in Values(icons)) yield return path;
        if (manifest.TryGetProperty("content_scripts", out JsonElement scripts))
            foreach (JsonElement script in scripts.EnumerateArray())
                foreach (string field in new[] { "js", "css" })
                    if (script.TryGetProperty(field, out JsonElement files))
                        foreach (string path in Values(files)) yield return path;
        if (manifest.TryGetProperty("background", out JsonElement background))
            foreach (string field in new[] { "service_worker", "scripts", "page" })
                if (background.TryGetProperty(field, out JsonElement files))
                    foreach (string path in Values(files)) yield return path;
        foreach (string action in new[] { "action", "browser_action", "page_action" })
            if (manifest.TryGetProperty(action, out JsonElement value))
                foreach (string field in new[] { "default_popup", "default_icon" })
                    if (value.TryGetProperty(field, out JsonElement files))
                        foreach (string path in Values(files)) yield return path;
        if (manifest.TryGetProperty("options_ui", out JsonElement options) &&
            options.TryGetProperty("page", out JsonElement page))
            foreach (string path in Values(page)) yield return path;
        if (manifest.TryGetProperty("options_page", out JsonElement optionsPage))
            foreach (string path in Values(optionsPage)) yield return path;
    }

    private static IEnumerable<string> Values(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => [value.GetString()!],
        JsonValueKind.Array => value.EnumerateArray().SelectMany(Values),
        JsonValueKind.Object => value.EnumerateObject().SelectMany(field =>
            Values(field.Value)),
        _ => throw new InvalidDataException("Unsupported extension manifest file reference."),
    };

    private static string Text(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement text) &&
        text.ValueKind == JsonValueKind.String ? text.GetString()! : "";
}

internal sealed record BrowserExtensionExpectation(string Name, string Version, string Label);
