using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace WorkflowDelivery.Repository;

internal sealed record RecordTaskOwner(string Script, string Rule);
internal sealed record CiScopeApplicability(bool Mise, bool AzureAuth, bool Scholarly,
    bool NuGetReproducibility, SortedDictionary<string, string[]> Reasons);

// Finite retained-job applicability and repository responsibility for changed coordinates.
// Native .NET/PNPM/Python/Ruby facts still own product consumption; a present coordinate
// without any responsibility or native consumer remains an explicit planning failure.
internal static partial class CiScope
{
    internal const string FamilyCatalog = "docs/governance/record-families.yaml";
    // check_repository_records.py imports this matcher; the record tasks own it.
    internal const string RecordMatcher = "eng/scripts/repository_path_patterns.py";
    private const string Azure = "src/private/app/azureauth-credprovider";
    private const string Scholarly = "src/private/lib/scholarly-publication";
    private const string Products = "tests/private/app/workflow-delivery/fixtures/products";
    private const string NuGetProduct = Products + "/hcoona-release-smoke-github-packages";
    // Inputs of every retained job: the workflow itself and its locked tools.
    private static readonly string[] AllInputs = [".github/workflows/ci.yml",
        "eng/scripts/Bootstrap-CiControl.ps1", "mise.toml", "mise.lock"];
    private static readonly string[] PythonInputs = ["pyproject.toml", "uv.lock", "uv.toml",
        ".python-version", ".config/uv/uv.toml", "conftest.py", "pytest.ini",
        "eng/scripts/sync_python_version.py", "eng/scripts/test-python.sh"];
    private static readonly string[] NuGetScenarioRoots = ["src/private/app/workflow-delivery",
        "tests/private/app/workflow-delivery", NuGetProduct];
    private static readonly string[] RubyInputs = ["Gemfile", "Gemfile.lock", ".ruby-version"];
    // Release scripts and workflow sources read by tests/eng/test_legacy_release_contract.py.
    private static readonly string[] LegacyReleaseScripts = [
        "eng/scripts/publish_node_gpr_idempotent.sh",
        "eng/scripts/publish_node_npmjs_idempotent.sh",
        "eng/scripts/release_orchestrate_lint_caller_completeness.sh",
        "eng/scripts/release_orchestrate_policy_validate_inputs.sh"];

    internal static CiScopeApplicability Select(string[] changed, GitComparison comparison,
        bool full, string[] resourceInputs)
    {
        string[] gems = GemRoots(comparison.Basis).Union(GemRoots(comparison.Candidate),
            StringComparer.Ordinal).ToArray();
        var reasons = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal)
        {
            ["validation"] = new(StringComparer.Ordinal) { "source conformance" },
            ["mise"] = new(StringComparer.Ordinal),
            ["azureauth"] = new(StringComparer.Ordinal),
            ["scholarly"] = new(StringComparer.Ordinal),
        };
        bool nuget = full;
        foreach (string path in changed)
        {
            foreach (string job in Jobs(path, gems).Where(reasons.ContainsKey))
                reasons[job].Add(path);
            nuget |= AllInputs.Contains(path, StringComparer.Ordinal) ||
                path == "stylecop.json" || NuGetScenarioInput(path) ||
                resourceInputs.Contains(path, StringComparer.Ordinal);
        }
        if (full)
            foreach (SortedSet<string> why in reasons.Values) why.Add("explicit full validation");
        return new(reasons["mise"].Count != 0, reasons["azureauth"].Count != 0,
            reasons["scholarly"].Count != 0, nuget, new(reasons.ToDictionary(pair => pair.Key,
                pair => pair.Value.ToArray()), StringComparer.Ordinal));
    }

    internal static async Task<ScopeCoordinate[]> OwnersAsync(GitMaterialization checkout,
        string[] changed, string[] resourceInputs,
        Func<string, CancellationToken, Task<string>> miseTasks, CancellationToken token)
    {
        var entries = checkout.Revision.Entries.ToDictionary(entry => entry.Path,
            StringComparer.Ordinal);
        if (!changed.Any(entries.ContainsKey))
            return changed.Order(StringComparer.Ordinal).Select(path =>
                new ScopeCoordinate(path, false, null, [])).ToArray();
        string? catalog = await checkout.ReadOptionalTextAsync(FamilyCatalog, token);
        RecordTaskOwner[] tasks = RecordTasks(await miseTasks(checkout.Root, token));
        return Owners(checkout.Revision, changed, resourceInputs,
            catalog is null ? [] : Bindings(catalog), tasks);
    }

    internal static ScopeCoordinate[] Owners(GitRevision revision, string[] changed,
        string[] resourceInputs, (string Id, string Pattern)[] bindings,
        RecordTaskOwner[] tasks)
    {
        var entries = revision.Entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        string[] gems = GemRoots(revision);
        string[] manifests = revision.Entries.Select(entry => entry.Path).Where(path =>
            path.EndsWith(".gemspec", StringComparison.Ordinal) ||
            path.EndsWith("/pyproject.toml", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
        var rows = new List<ScopeCoordinate>();
        foreach (string path in changed.Order(StringComparer.Ordinal))
        {
            if (!entries.TryGetValue(path, out GitEntry? entry))
            {
                rows.Add(new(path, false, null, []));
                continue;
            }
            var reasons = new List<ScopeResponsibility>();
            void Add(string owner, string target, string rule, params string[] sources)
            {
                string[] committed = sources.Where(Regular).Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal).ToArray();
                if (committed.Length != 0) reasons.Add(new(owner, target, rule, committed));
            }
            if (path == ".github/CODEOWNERS")
                Add("github", path, "native-codeowners-routing", path);
            if (path == "hk.pkl" && Regular(".github/workflows/ci.yml"))
                Add("conformance", ".github/workflows/ci.yml#conformance",
                    "native-hk-configuration", ".github/workflows/ci.yml", path);
            if (path == "eng/scripts/update_node_mise_lock.py" && Regular("hk.pkl") &&
                Regular(".github/workflows/ci.yml"))
                Add("conformance", ".github/workflows/ci.yml#conformance",
                    "native-hk-node-mise-authority", ".github/workflows/ci.yml", "hk.pkl", path);
            if (path == "renovate.json" && Regular("pyproject.toml") &&
                Regular("tests/eng/test_python_runtime.py"))
                Add("python", "pytest:tests/eng/test_python_runtime.py",
                    "configured-python-runtime-input", "pyproject.toml",
                    "tests/eng/test_python_runtime.py");
            string[] jobs = Jobs(path, gems);
            foreach (string manifest in manifests.Where(manifest =>
                manifest.EndsWith(".gemspec", StringComparison.Ordinal)))
                if (jobs.Contains("ruby") && (AllInputs.Contains(path, StringComparer.Ordinal) ||
                    RubyInputs.Contains(path, StringComparer.Ordinal) ||
                    Under(path, Parent(manifest))))
                    Add("ruby", manifest, "retained-gem-input", manifest, "Gemfile");
            if (jobs.Contains("mise"))
                Add("mise", ".github/workflows/ci.yml#mise-lock", "retained-special-job-input",
                    "mise.toml", path);
            if (jobs.Contains("azureauth"))
                Add("azureauth", Azure, "retained-special-job-input", "dirs.proj",
                    Azure + "/python/pyproject.toml");
            if (jobs.Contains("scholarly"))
                Add("scholarly", Scholarly, "retained-special-job-input",
                    Scholarly + "/pyproject.toml", "apm.yml");
            // This only tells another ecosystem that a coordinate has a Python consumer.
            // The Linux Python group ignores Python-only reasons and plans natively.
            string[] python = manifests.Where(manifest =>
                manifest.EndsWith("/pyproject.toml", StringComparison.Ordinal) &&
                Under(path, Parent(manifest))).ToArray();
            if (python.Length != 0 || PythonResponsibility(path))
                Add("python", ".github/workflows/ci.yml#python-tests",
                    "native-python-caller-responsibility", ["pyproject.toml", .. python]);
            if (resourceInputs.Contains(path, StringComparer.Ordinal))
                Add("dotnet", ControlTestInputs.Project, "native-embedded-resource",
                    ControlTestInputs.Project);
            foreach ((string id, string pattern) in bindings)
                if (Matches(path, pattern))
                    Add("record-system", path, "record-binding:" + id, FamilyCatalog);
            foreach (RecordTaskOwner task in tasks)
                if (task.Script == path ||
                    (path == RecordMatcher && task.Rule.StartsWith("record-control:",
                        StringComparison.Ordinal)))
                    Add("record-system", path, task.Rule, "mise.toml");
            rows.Add(new(path, true, entry.Mode, reasons.Distinct()
                .OrderBy(reason => reason.Owner, StringComparer.Ordinal)
                .ThenBy(reason => reason.Target, StringComparer.Ordinal)
                .ThenBy(reason => reason.Rule, StringComparer.Ordinal).ToArray()));
        }
        return rows.ToArray();

        bool Regular(string path) => entries.TryGetValue(path, out GitEntry? item) &&
            item.ObjectType == "blob" && item.Mode is "100644" or "100755";
    }

    // Reads only bindings[].{id,state,path}. The record checker remains the catalog authority;
    // a binding-format change must update both readers.
    internal static (string Id, string Pattern)[] Bindings(string catalog)
    {
        object? document;
        try
        {
            using var reader = new StringReader(catalog);
            document = new DeserializerBuilder().Build().Deserialize<object?>(
                new MergingParser(new Parser(reader)));
        }
        catch (YamlException exception)
        { throw new InvalidDataException("Unreadable record-family catalog.", exception); }
        if (document is not IDictionary<object, object?> root ||
            !root.TryGetValue("bindings", out object? value) || value is not IList<object?> list)
            throw new InvalidDataException("The record-family catalog has no bindings.");
        var bindings = new List<(string, string)>();
        foreach (object? item in list)
        {
            if (item is not IDictionary<object, object?> binding ||
                !binding.TryGetValue("id", out object? id) || id is not string name ||
                !binding.TryGetValue("state", out object? state) || state is not string text ||
                !binding.TryGetValue("path", out object? path) || path is not string pattern)
                throw new InvalidDataException("A record binding lacks its id, state or path.");
            if (text == "current") bindings.Add((name, pattern));
        }
        return bindings.ToArray();
    }

    // Matches the record checker's component-aware star globs: '*' within one component and
    // '**/' across zero or more directories (pinned by tests over every current binding).
    internal static bool Matches(string path, string pattern)
    {
        var matcher = new Matcher(StringComparison.Ordinal);
        matcher.AddInclude(pattern);
        return matcher.Match(path).HasMatches;
    }

    // Native mise task metadata identifies the record checker and regression scripts.
    internal static RecordTaskOwner[] RecordTasks(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Native mise task list is not an array.");
        var owners = new List<RecordTaskOwner>();
        foreach (JsonElement task in document.RootElement.EnumerateArray())
        {
            if (task.ValueKind != JsonValueKind.Object ||
                !task.TryGetProperty("name", out JsonElement name)) continue;
            string? rule = name.GetString() switch
            {
                "//:records:check" => "record-control:repository-record-validation",
                "//:records:test" => "record-regression:records:test",
                _ => null,
            };
            if (rule is null) continue;
            Match command = task.TryGetProperty("run", out JsonElement run) &&
                run.ValueKind == JsonValueKind.Array && run.GetArrayLength() == 1 &&
                run[0].ValueKind == JsonValueKind.String
                ? UvScript().Match(run[0].GetString()!)
                : System.Text.RegularExpressions.Match.Empty;
            if (!command.Success)
                throw new InvalidDataException("Unsupported native record task: " +
                    name.GetString());
            ImpactPlanner.ValidatePath(command.Groups[1].Value);
            owners.Add(new(command.Groups[1].Value, rule));
        }
        return owners.ToArray();
    }

    internal static Task<string> MiseTasksAsync(string directory, CancellationToken token) =>
        NativeProcess.RunAsync("mise", directory, ["tasks", "ls", "--json"], token);

    internal static bool LegacyReleaseInput(string path) =>
        Under(path, ".github/workflows") ||
        path.EndsWith("/three.release.yml", StringComparison.Ordinal) ||
        LegacyReleaseScripts.Contains(path, StringComparer.Ordinal);

    private static string[] Jobs(string path, string[] gems)
    {
        if (AllInputs.Contains(path, StringComparer.Ordinal))
            return ["mise", "azureauth", "ruby", "scholarly"];
        var jobs = new List<string>();
        if (Name(path) is "mise.toml" or "mise.lock") jobs.Add("mise");
        if (AzureInput(path)) jobs.Add("azureauth");
        if (RubyInputs.Contains(path, StringComparer.Ordinal) ||
            gems.Any(root => Under(path, root))) jobs.Add("ruby");
        if (Under(path, Scholarly) ||
            path.StartsWith(".agents/skills/scholarly-", StringComparison.Ordinal) ||
            path is "apm.yml" or "apm.lock.yaml") jobs.Add("scholarly");
        return jobs.ToArray();
    }

    private static bool AzureInput(string path) =>
        DotNetInheritedInputs.Consumes(path, [Azure, "tests/private/app/azureauth-credprovider"]) ||
        Under(path, "eng/scripts/azureauth-credprovider") ||
        Under(path, "src/public/lib/nbgv-python") ||
        PythonInputs.Contains(path, StringComparer.Ordinal);

    private static bool NuGetScenarioInput(string path) =>
        Under(path, Products) && !Under(path, NuGetProduct)
            ? DotNetInheritedInputs.Consumes(path, [NuGetProduct])
            : DotNetInheritedInputs.Consumes(path, NuGetScenarioRoots);

    private static bool PythonResponsibility(string path) =>
        (path.EndsWith(".py", StringComparison.Ordinal) && path is not
            ("eng/scripts/check_repository_records.py" or
             "eng/scripts/update_node_mise_lock.py" or RecordMatcher)) ||
        AllInputs.Contains(path, StringComparer.Ordinal) ||
        PythonInputs.Contains(path, StringComparer.Ordinal) || Under(path, "tests/eng") ||
        path is ".github/workflows/workflow-delivery-v3-python-smoke.yml" or
            ".github/workflows/workflow-delivery-v3-ruby-smoke.yml" or ".typos.toml" ||
        AzureInput(path) || LegacyReleaseInput(path);

    private static string[] GemRoots(GitRevision revision) => revision.Entries
        .Select(entry => entry.Path).Where(path => path.EndsWith(".gemspec",
            StringComparison.Ordinal)).Select(Parent).Distinct(StringComparer.Ordinal).ToArray();

    private static string Parent(string path)
    {
        int separator = path.LastIndexOf('/');
        return separator < 0 ? "" : path[..separator];
    }

    private static string Name(string path) => path[(path.LastIndexOf('/') + 1)..];

    private static bool Under(string path, string directory) => directory.Length == 0 ||
        path == directory || path.StartsWith(directory + "/", StringComparison.Ordinal);

    [GeneratedRegex(@"^uv run --script ([A-Za-z0-9_./-]+\.py)$")]
    private static partial Regex UvScript();
}
