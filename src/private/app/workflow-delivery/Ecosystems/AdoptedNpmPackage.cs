using System.Text.Json;

namespace WorkflowDelivery.Ecosystems;

// Required metadata belongs to the adopted product recipe, not universal npm policy.
internal static class AdoptedNpmPackage
{
    internal const string PackageName = "@hcoona/hcoona-release-smoke-npm";

    internal static void ValidateMetadata(string text, string version)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        JsonElement metadata = document.RootElement;
        if (metadata.ValueKind != JsonValueKind.Object ||
            metadata.EnumerateObject().Select(field => field.Name).Distinct(StringComparer.Ordinal)
                .Count() != metadata.EnumerateObject().Count())
            throw new InvalidDataException("Native packed metadata must be an unambiguous object.");
        Require("name", PackageName);
        Require("version", version);
        Require("type", "module");
        Require("main", "./dist/index.js");
        foreach (string field in new[] { "dependencies", "optionalDependencies", "peerDependencies",
            "peerDependenciesMeta" })
            if (metadata.TryGetProperty(field, out JsonElement value) &&
                (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Any()))
                throw new InvalidDataException(
                    "Unsupported installed npm dependency shape: " + field);
        foreach (string field in new[] { "bundledDependencies", "bundleDependencies" })
            if (metadata.TryGetProperty(field, out JsonElement value) &&
                value.ValueKind != JsonValueKind.False &&
                (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 0))
                throw new InvalidDataException("Unsupported bundled npm dependency shape.");
        if (metadata.TryGetProperty("devDependencies", out JsonElement development) &&
            development.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Unsupported npm development dependency metadata.");
        if (metadata.TryGetProperty("engines.runtime", out _) ||
            (metadata.TryGetProperty("gypfile", out JsonElement gypfile) &&
                gypfile.ValueKind != JsonValueKind.False))
            throw new InvalidDataException("Unsupported npm runtime/native-build metadata.");
        if (metadata.TryGetProperty("scripts", out JsonElement scripts))
        {
            if (scripts.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Unsupported packed npm script metadata.");
            foreach (string script in new[] { "preinstall", "install", "postinstall" })
                if (scripts.TryGetProperty(script, out _))
                    throw new InvalidDataException(
                        "Unsupported installed npm lifecycle: " + script);
        }

        void Require(string field, string expected)
        {
            if (!metadata.TryGetProperty(field, out JsonElement value) ||
                value.ValueKind != JsonValueKind.String || value.GetString() != expected)
                throw new InvalidDataException("Unexpected native packed npm " + field + ".");
        }
    }

}
