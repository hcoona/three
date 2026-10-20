using System.Text.Json;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Ecosystems;

// Required metadata belongs to the adopted product recipe, not universal npm policy.
internal static class AdoptedNpmPackage
{
    internal const string PackageName = "@hcoona/hcoona-release-smoke-npm";

    internal static void ValidateMetadata(string text, string version) =>
        ValidateMetadata(text, version, PackageName, "./dist/index.js",
            registryDependencies: false);

    // Registry dependencies require the consumer to resolve them from the public registry.
    internal static void ValidateMetadata(string text, string version, string name,
        string main, bool registryDependencies)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        JsonElement metadata = document.RootElement;
        if (metadata.ValueKind != JsonValueKind.Object ||
            metadata.EnumerateObject().Select(field => field.Name).Distinct(StringComparer.Ordinal)
                .Count() != metadata.EnumerateObject().Count())
            throw new InvalidDataException("Native packed metadata must be an unambiguous object.");
        Require("name", name);
        Require("version", version);
        Require("type", "module");
        Require("main", main);
        foreach (string field in new[] { "dependencies", "optionalDependencies", "peerDependencies",
            "peerDependenciesMeta" })
            if (metadata.TryGetProperty(field, out JsonElement value) &&
                (value.ValueKind != JsonValueKind.Object ||
                    (!registryDependencies && value.EnumerateObject().Any())))
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


    internal static NativeCommand Import(string directory) => new("node",
        Path.Combine(directory, "consumer"),
        ["--input-type=module", "--eval", "import { strict as assert } from 'node:assert';"
            + "import { smokeMessage } from '@hcoona/hcoona-release-smoke-npm';"
            + "assert.equal(smokeMessage(), 'hcoona-release-smoke-npm');"], 30);
}
