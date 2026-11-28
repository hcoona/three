using System.Text.Json;
using System.Text.Json.Serialization;
using WorkflowDelivery.CI.Callers;
using WorkflowDelivery.Ecosystems;
using WorkflowDelivery.Platform;
using WorkflowDelivery.Repository;

namespace WorkflowDelivery.CI;

[JsonConverter(typeof(JsonStringEnumConverter<ModelChecks>))]
internal enum ModelChecks { None, Callers, Full }

internal sealed record NativePreparationSelection(CiComparison Comparison,
    bool Python, bool Ruby, bool Node, bool Browser, ModelChecks Models);

// The native Linux scenarios own preparation; this boundary only selects them. The runner's
// filter, per-module minimum count and exit code qualify the selected rows.
internal static class NativePreparationQualification
{
    private static readonly string[] Shared = [".github/workflows/ci.yml", "mise.toml",
        "mise.lock", "workflow-delivery.quality.yml", CiScope.SkippedTestSummary];
    private static readonly string[] NodeInputs = ["package.json", "pnpm-workspace.yaml",
        "pnpm-lock.yaml", ".npmrc", .. NodeRepositoryReader.HookFiles];
    private static readonly string[] PythonProductRoots =
    [.. ReleaseUnitCatalog.PythonUnits
        .Where(u => !u.Declaration.StartsWith("tests/", StringComparison.Ordinal))
        .Select(u => ReleaseUnitCatalog.ParentDirectory(u.Declaration))];
    private static readonly string[] PythonSmokeRoots =
    [.. ReleaseUnitCatalog.PythonUnits
        .Where(u => u.Declaration.StartsWith("tests/", StringComparison.Ordinal))
        .Select(u => ReleaseUnitCatalog.ParentDirectory(u.Declaration))];
    private static readonly string[] RubySmokeRoots =
    [.. ReleaseUnitCatalog.RubyUnits
        .Where(u => u.Declaration.StartsWith("tests/", StringComparison.Ordinal))
        .Select(u => ReleaseUnitCatalog.ParentDirectory(u.Declaration))];
    private static readonly string[] BrowserProductRoots =
        [.. ReleaseUnitCatalog.BrowserStoreUnits.Select(u => u.ProductDirectory)];

    internal static NativePreparationSelection Select(CiComparison comparison,
        string[] changedPaths)
    {
        foreach (string path in changedPaths) ImpactPlanner.ValidatePath(path);
        if (comparison.Full && comparison.Basis != comparison.Candidate)
            throw new InvalidDataException("Full native qualification requires one source.");
        return new(comparison,
            comparison.Full || changedPaths.Any(path => Input(path, true)),
            comparison.Full || changedPaths.Any(path => Input(path, false)),
            comparison.Full || changedPaths.Any(NodeInput),
            comparison.Full || changedPaths.Any(BrowserInput),
            comparison.Full ? ModelChecks.Full : changedPaths.Select(CiScope.DeliveryModelInput)
                .DefaultIfEmpty(ModelChecks.None).Max());
    }

    internal static int? Run(string[] args, TextWriter output)
    {
        if (args is ["ci", "native-preparation-plan", var checkout, var scopePath,
                var directory])
        {
            string json = File.ReadAllText(scopePath);
            using JsonDocument document = JsonDocument.Parse(json);
            string basis = CiCallerTransfer.ScopeCommit(document.RootElement, "base");
            string candidate = CiCallerTransfer.ScopeCommit(document.RootElement, "candidate");
            bool full = CiCallerTransfer.Full(document.RootElement);
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
                    $"node={selected.Node.ToString().ToLowerInvariant()}\n" +
                    $"browser={selected.Browser.ToString().ToLowerInvariant()}\n" +
                    $"models={selected.Models.ToString().ToLowerInvariant()}\n");
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
        string[] roots = python
            ? [.. CiScope.ManagedControlRoots, .. PythonProductRoots]
            : CiScope.ManagedControlRoots;
        string[] smokeRoots = python ? PythonSmokeRoots : RubySmokeRoots;
        string directory = CiScope.Parent(path);
        if (path[(path.LastIndexOf('/') + 1)..] == "workflow-delivery.quality.yml")
            return smokeRoots.Any(smoke => CiScope.Under(smoke, directory));
        return DotNetInheritedInputs.Consumes(path, roots) ||
            smokeRoots.Any(smoke => CiScope.Under(path, smoke));
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
            DotNetInheritedInputs.Consumes(path, CiScope.ManagedControlRoots);
    }

    // The browser-store scenario also builds the committed product, and the AMO source archive
    // carries every product file.
    private static bool BrowserInput(string path) => NodeInput(path) ||
        BrowserProductRoots.Any(root => CiScope.Under(path, root));
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(NativePreparationSelection))]
internal partial class NativePreparationJson : JsonSerializerContext;
