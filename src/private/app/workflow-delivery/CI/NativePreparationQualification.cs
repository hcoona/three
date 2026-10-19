using System.Text.Json;
using System.Text.Json.Serialization;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI;

internal sealed record NativePreparationSelection(CiComparison Comparison,
    bool Python, bool Ruby, bool Node);

// The native Linux scenarios own preparation; this boundary only selects them. The runner's
// filter, per-module minimum count and exit code qualify the selected rows.
internal static class NativePreparationQualification
{
    private const string Products = "tests/private/app/workflow-delivery/fixtures/products/";
    private static readonly string[] Shared = [".github/workflows/ci.yml", "mise.toml",
        "mise.lock", "workflow-delivery.quality.yml"];
    private static readonly string[] Managed = ["src/private/app/workflow-delivery",
        "tests/private/app/workflow-delivery"];
    // The root PNPM workspace files the Node contract tests' tools read: the locked PNPM
    // resolves its settings and hooks from them, beside the fixtures under Managed.
    private static readonly string[] NodeInputs = ["package.json", "pnpm-workspace.yaml",
        "pnpm-lock.yaml", ".npmrc", .. NodeRepositoryReader.HookFiles];

    internal static NativePreparationSelection Select(CiComparison comparison,
        string[] changedPaths)
    {
        foreach (string path in changedPaths) ImpactPlanner.ValidatePath(path);
        if (comparison.Full && comparison.Basis != comparison.Candidate)
            throw new InvalidDataException("Full native qualification requires one source.");
        return new(comparison,
            comparison.Full || changedPaths.Any(path => Input(path, true)),
            comparison.Full || changedPaths.Any(path => Input(path, false)),
            comparison.Full || changedPaths.Any(NodeInput));
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
                    $"ruby={selected.Ruby.ToString().ToLowerInvariant()}\n" +
                    $"node={selected.Node.ToString().ToLowerInvariant()}\n");
            return 0;
        }
        return null;
    }

    private static bool Input(string path, bool python)
    {
        if (ControlInputs.InertDocument(path)) return false;
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

    // The PNPM/Node contract tests run the locked Node and PNPM through the managed readers and
    // package operations on committed fixtures; they consume no product source or quality
    // declaration.
    private static bool NodeInput(string path)
    {
        if (ControlInputs.InertDocument(path)) return false;
        if (Shared.Contains(path, StringComparer.Ordinal)) return true;
        if (path.EndsWith("/workflow-delivery.quality.yml", StringComparison.Ordinal))
            return false;
        return NodeInputs.Contains(path, StringComparer.Ordinal) ||
            DotNetInheritedInputs.Consumes(path, Managed);
    }

    private static bool Under(string path, string directory) => path == directory ||
        path.StartsWith(directory + "/", StringComparison.Ordinal);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(NativePreparationSelection))]
internal partial class NativePreparationJson : JsonSerializerContext;
