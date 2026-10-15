using System.Text.Json;
using System.Text.Json.Serialization;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI;

internal sealed record NativePreparationSelection(CiComparison Comparison,
    bool Python, bool Ruby);

// The native Linux scenarios own preparation; this boundary only selects them. The runner's
// filter, per-module minimum count and exit code qualify the selected rows.
internal static class NativePreparationQualification
{
    private const string Products = "tests/private/app/workflow-delivery/fixtures/products/";
    private static readonly string[] Shared = [".github/workflows/ci.yml", "mise.toml",
        "mise.lock", "workflow-delivery.quality.yml"];
    private static readonly string[] Managed = ["src/private/app/workflow-delivery",
        "tests/private/app/workflow-delivery"];

    internal static NativePreparationSelection Select(CiComparison comparison,
        string[] changedPaths)
    {
        foreach (string path in changedPaths) ImpactPlanner.ValidatePath(path);
        if (comparison.Full && comparison.Basis != comparison.Candidate)
            throw new InvalidDataException("Full native qualification requires one source.");
        return new(comparison,
            comparison.Full || changedPaths.Any(path => Input(path, true)),
            comparison.Full || changedPaths.Any(path => Input(path, false)));
    }

    internal static int? Run(string[] args, TextWriter output)
    {
        if (args is ["ci", "native-preparation-plan", var checkout, var scopePath,
                var directory])
        {
            string json = File.ReadAllText(scopePath);
            using JsonDocument document = JsonDocument.Parse(json);
            string basis = document.RootElement.GetProperty("base").GetString()
                ?? throw new InvalidDataException("Missing native comparison basis.");
            string candidate = document.RootElement.GetProperty("candidate").GetString()
                ?? throw new InvalidDataException("Missing native comparison candidate.");
            bool full = document.RootElement.GetProperty("full").GetBoolean();
            GitComparison comparison = new GitReader(checkout).CompareAsync(basis, candidate,
                CancellationToken.None).GetAwaiter().GetResult();
            GitMaterialization.BindAsync(checkout, comparison.Candidate, CancellationToken.None)
                .GetAwaiter().GetResult();
            ScopeInputs scope = ScopeInputs.Read(json, comparison, full);
            NativePreparationSelection selected = Select(scope.Comparison, scope.ChangedPaths);
            Directory.CreateDirectory(directory);
            string result = JsonSerializer.Serialize(selected,
                NativePreparationJson.Default.NativePreparationSelection);
            File.WriteAllText(Path.Combine(directory, "plan.json"), result);
            output.WriteLine(result);
            if (Environment.GetEnvironmentVariable("GITHUB_OUTPUT") is { Length: > 0 } path)
                File.AppendAllText(path,
                    $"python={selected.Python.ToString().ToLowerInvariant()}\n" +
                    $"ruby={selected.Ruby.ToString().ToLowerInvariant()}\n");
            return 0;
        }
        return null;
    }

    private static bool Input(string path, bool python)
    {
        if (path == "src/private/app/workflow-delivery/README.md" ||
            path.StartsWith("src/private/app/workflow-delivery/docs/", StringComparison.Ordinal))
            return false;
        if (Shared.Contains(path, StringComparer.Ordinal)) return true;
        if (python && path is "pyproject.toml" or "uv.lock" or "uv.toml" or
                ".config/uv/uv.toml" or ".python-version") return true;
        string[] roots = python ? [.. Managed, "src/public/lib/nbgv-python"] : Managed;
        int separator = path.LastIndexOf('/');
        string name = path[(separator + 1)..];
        string directory = separator < 0 ? "" : path[..separator];
        if (name == "workflow-delivery.quality.yml")
            return Under(Products + (python ? "hcoona-release-smoke-python" :
                "hcoona-release-smoke-ruby"), directory);
        return DotNetInheritedInputs.Consumes(path, roots) ||
            Under(path, Products + (python ? "hcoona-release-smoke-python" :
                "hcoona-release-smoke-ruby"));
    }

    private static bool Under(string path, string directory) => path == directory ||
        path.StartsWith(directory + "/", StringComparison.Ordinal);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(NativePreparationSelection))]
internal partial class NativePreparationJson : JsonSerializerContext;
