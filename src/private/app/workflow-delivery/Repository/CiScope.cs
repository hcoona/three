using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.FileSystemGlobbing;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace WorkflowDelivery.Repository;

internal sealed record RecordTaskOwner(string Script, string Rule);
// Committed root HK and Renovate configuration text read at one endpoint.
internal sealed record ToolingConfiguration(string? Hk, string? Renovate);
internal sealed record CiScopeApplicability(bool Mise, bool AzureAuth, bool Scholarly,
    bool NuGetNativePreparation, SortedDictionary<string, string[]> Reasons);

// Finite retained-job applicability and repository responsibility for changed coordinates.
// Native .NET/PNPM/Python/Ruby facts still own product consumption; a present coordinate
// without any responsibility or native consumer remains an explicit planning failure.
internal static partial class CiScope
{
    internal const string FamilyCatalog = "docs/governance/record-families.yaml";
    // check_repository_records.py imports this matcher; the record-control task owns it.
    internal const string RecordMatcher = "eng/scripts/repository_path_patterns.py";
    // The root HK configuration; native Pkl imports name the local modules it consumes.
    internal const string HkConfiguration = "hk.pkl";
    internal const string RenovateConfiguration = "renovate.json";
    private const string RenovateWorkflow = ".github/workflows/renovate.yml";
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
    private static readonly string[] NuGetPreparationRoots = ["src/private/app/workflow-delivery",
        "tests/private/app/workflow-delivery", NuGetProduct];
    private static readonly string[] RubyInputs = ["Gemfile", "Gemfile.lock", ".ruby-version"];

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
                path == "stylecop.json" || NuGetPreparationInput(path) ||
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
        Func<string, CancellationToken, Task<string>> miseTasks,
        Func<string, CancellationToken, Task<string>> hkImports, CancellationToken token)
    {
        var entries = checkout.Revision.Entries.ToDictionary(entry => entry.Path,
            StringComparer.Ordinal);
        if (!changed.Any(entries.ContainsKey))
            return changed.Order(StringComparer.Ordinal).Select(path =>
                new ScopeCoordinate(path, false, null, [])).ToArray();
        string? catalog = await checkout.ReadOptionalTextAsync(FamilyCatalog, token);
        RecordTaskOwner[] tasks = RecordTasks(await miseTasks(checkout.Root, token),
            checkout.Root);
        string[] hk = checkout.Revision.Entries.Any(entry => entry.Path == HkConfiguration)
            ? HkModules(await hkImports(checkout.Root, token), checkout.Root)
            : [];
        var tooling = new ToolingConfiguration(
            await checkout.ReadOptionalTextAsync(HkConfiguration, token),
            await checkout.ReadOptionalTextAsync(RenovateConfiguration, token));
        return Owners(checkout.Revision, changed, resourceInputs,
            catalog is null ? [] : Bindings(catalog), tasks, hk, tooling);
    }

    internal static ScopeCoordinate[] Owners(GitRevision revision, string[] changed,
        string[] resourceInputs, (string Id, string Pattern)[] bindings,
        RecordTaskOwner[] tasks, string[] hkModules, ToolingConfiguration? tooling = null)
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
            if (RenovateInput(path))
                Add("renovate", RenovateWorkflow, "native-renovate-configuration", path);
            if (tooling is not null)
                foreach ((string source, string? text) in new[] {
                    (HkConfiguration, tooling.Hk), (RenovateConfiguration, tooling.Renovate) })
                    if (text is not null && ReferencesPath(text, path) &&
                        Regular(".github/workflows/ci.yml"))
                        Add("conformance", ".github/workflows/ci.yml#conformance",
                            "literal-tooling-reference", ".github/workflows/ci.yml", source);
            if (hkModules.Contains(path, StringComparer.Ordinal) &&
                Regular(".github/workflows/ci.yml"))
                Add("conformance", ".github/workflows/ci.yml#conformance",
                    "native-hk-configuration", ".github/workflows/ci.yml", path);
            // Fixed copy of HK's actionlint glob for the root workflow directory; the drift
            // test in tests/eng/test_general_ci_workflow.py keeps the step in place.
            if (RootWorkflow(path) && Regular(HkConfiguration) &&
                Regular(".github/workflows/ci.yml"))
                Add("conformance", ".github/workflows/ci.yml#conformance",
                    "native-hk-workflow-lint", ".github/workflows/ci.yml", HkConfiguration);
            // Fixed copy of HK's shellcheck/shfmt glob and shell-relevant exclusions; the
            // drift test in tests/eng/test_general_ci_workflow.py keeps them aligned.
            if (LintedShell(path) && Regular(HkConfiguration) &&
                Regular(".github/workflows/ci.yml"))
                Add("conformance", ".github/workflows/ci.yml#conformance",
                    "native-hk-shell-lint", ".github/workflows/ci.yml", HkConfiguration);
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
            // Record-task scripts, and the matcher the checker imports, run under their mise
            // tasks ('uv run --script'), not as Python group targets.
            RecordTaskOwner[] recordTasks = tasks.Where(task => task.Script == path ||
                (path == RecordMatcher && task.Rule.StartsWith("record-control:",
                    StringComparison.Ordinal))).ToArray();
            // This only tells another ecosystem that a coordinate has a Python consumer.
            // The Linux Python group ignores Python-only reasons and plans natively.
            string[] python = manifests.Where(manifest =>
                manifest.EndsWith("/pyproject.toml", StringComparison.Ordinal) &&
                Under(path, Parent(manifest))).ToArray();
            if (recordTasks.Length == 0 && (python.Length != 0 || PythonResponsibility(path)))
                Add("python", ".github/workflows/ci.yml#python-tests",
                    "native-python-caller-responsibility", ["pyproject.toml", .. python]);
            if (resourceInputs.Contains(path, StringComparer.Ordinal))
                Add("dotnet", ControlTestInputs.Project, "native-embedded-resource",
                    ControlTestInputs.Project);
            foreach ((string id, string pattern) in bindings)
                if (Matches(path, pattern))
                    Add("record-system", path, "record-binding:" + id, FamilyCatalog);
            foreach (RecordTaskOwner task in recordTasks)
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
            // '**' outside a whole component differs between the two matchers; fail closed.
            if (pattern.Split('/').Any(part => part.Contains("**", StringComparison.Ordinal) &&
                    part != "**"))
                throw new InvalidDataException("Unsupported record binding pattern: " + pattern);
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

    // Native mise task metadata identifies the record checker and regression scripts. Only
    // root-directory tasks are supported, so the script is repository-relative.
    internal static RecordTaskOwner[] RecordTasks(string json, string root)
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
            if (!task.TryGetProperty("dir", out JsonElement dir) ||
                dir.ValueKind != JsonValueKind.String ||
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir.GetString()!)) !=
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)))
                throw new InvalidDataException("Unsupported native record task directory: " +
                    name.GetString());
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
        NativeProcess.RunAsync("mise", directory, ["tasks", "ls", "--hidden", "--json"], token);

    // The endpoint's locked Pkl reports the root HK configuration's complete import graph.
    // '--root-dir' makes Pkl itself reject a file module outside the checkout.
    internal static Task<string> HkImportsAsync(string directory, CancellationToken token) =>
        NativeProcess.RunAsync("mise", directory, ["exec", "--locked", "pkl", "--", "pkl",
            "analyze", "imports", "--format", "json", "--root-dir", directory,
            HkConfiguration], token);

    // Repository-local modules of the native HK import graph. Remote package and standard
    // library modules are not repository coordinates. Any other or unreadable module fails.
    internal static string[] HkModules(string json, string root)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("imports", out JsonElement imports) ||
            imports.ValueKind != JsonValueKind.Object ||
            !document.RootElement.TryGetProperty("resolvedImports", out JsonElement resolved) ||
            resolved.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Native HK imports lack their module graph.");
        var uris = new List<string>();
        foreach (JsonProperty module in imports.EnumerateObject())
        {
            uris.Add(module.Name);
            if (module.Value.ValueKind != JsonValueKind.Array)
                throw new InvalidDataException("Native HK imports are not a module list.");
            foreach (JsonElement import in module.Value.EnumerateArray())
                uris.Add(import.ValueKind == JsonValueKind.Object &&
                    import.TryGetProperty("uri", out JsonElement uri) &&
                    uri.ValueKind == JsonValueKind.String
                    ? uri.GetString()!
                    : throw new InvalidDataException("A native HK import lacks its URI."));
        }
        foreach (JsonProperty module in resolved.EnumerateObject())
            uris.Add(module.Value.ValueKind == JsonValueKind.String
                ? module.Value.GetString()!
                : throw new InvalidDataException("A resolved HK import lacks its URI."));
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string text in uris)
        {
            if (!Uri.TryCreate(text, UriKind.Absolute, out Uri? uri))
                throw new InvalidDataException("Unreadable native HK module: " + text);
            if (uri.Scheme is "package" or "pkl") continue;
            if (!uri.IsFile)
                throw new InvalidDataException("Unsupported native HK module: " + text);
            string relative = Path.GetRelativePath(full, uri.LocalPath);
            if (Path.IsPathRooted(relative) || relative == ".." ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidDataException("Native HK module is outside the checkout: " +
                    text);
            relative = relative.Replace(Path.DirectorySeparatorChar, '/');
            ImpactPlanner.ValidatePath(relative);
            paths.Add(relative);
        }
        if (!paths.Contains(HkConfiguration))
            throw new InvalidDataException("Native HK imports omit the root configuration.");
        return paths.ToArray();
    }

    // Renovate's committed configuration and its dedicated workflow.
    private static bool RenovateInput(string path) =>
        path == RenovateConfiguration || path == RenovateWorkflow ||
        Under(path, ".github/renovate");

    // A complete repository-path token in committed tooling configuration: the characters
    // around it are not path characters, so 'PklProject' does not match
    // 'PklProject.deps.json' and '.editorconfig' does not match 'Steps.editorconfig_check'.
    // This records a literal reference; it does not interpret commands or prove a read.
    internal static bool ReferencesPath(string text, string path)
    {
        for (int index = text.IndexOf(path, StringComparison.Ordinal); index >= 0;
            index = text.IndexOf(path, index + 1, StringComparison.Ordinal))
        {
            int end = index + path.Length;
            if ((index == 0 || !PathCharacter(text[index - 1])) &&
                (end == text.Length || !PathCharacter(text[end])))
                return true;
        }
        return false;
    }

    private static bool PathCharacter(char value) => char.IsAsciiLetterOrDigit(value) ||
        value is '.' or '_' or '-' or '/' or '@' or '+' or '~';

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

    private static bool NuGetPreparationInput(string path) =>
        !ControlInputs.InertDocument(path) && (Under(path, Products) && !Under(path, NuGetProduct)
            ? DotNetInheritedInputs.Consumes(path, [NuGetProduct])
            : DotNetInheritedInputs.Consumes(path, NuGetPreparationRoots));

    internal static bool RootWorkflow(string path) =>
        path.StartsWith(".github/workflows/", StringComparison.Ordinal) &&
        path.IndexOf('/', ".github/workflows/".Length) < 0 &&
        (path.EndsWith(".yml", StringComparison.Ordinal) ||
            path.EndsWith(".yaml", StringComparison.Ordinal));

    // HK excludes **/.github/**, **/.specify/**, **/generated/** and root-only .agents/**.
    internal static bool LintedShell(string path)
    {
        if (!path.EndsWith(".sh", StringComparison.Ordinal) &&
            !path.EndsWith(".bash", StringComparison.Ordinal))
            return false;
        string[] directories = path.Split('/')[..^1];
        return !directories.Any(name => name is ".github" or ".specify" or "generated") &&
            !path.StartsWith(".agents/", StringComparison.Ordinal);
    }

    private static bool PythonResponsibility(string path) =>
        path.EndsWith(".py", StringComparison.Ordinal) ||
        AllInputs.Contains(path, StringComparer.Ordinal) ||
        PythonInputs.Contains(path, StringComparer.Ordinal) || Under(path, "tests/eng") ||
        path is ".github/workflows/workflow-delivery-v3-python-smoke.yml" or
            ".github/workflows/workflow-delivery-v3-ruby-smoke.yml" or ".typos.toml" ||
        PythonRepositoryInputs.BuddyGuardInputs.Contains(path, StringComparer.Ordinal) ||
        PythonRepositoryInputs.IsWorkflow(path) || PythonRepositoryInputs.HkInput(path) ||
        AzureInput(path);

    private static string[] GemRoots(GitRevision revision) => revision.Entries
        .Select(entry => entry.Path).Where(path => path.EndsWith(".gemspec",
            StringComparison.Ordinal)).Select(Parent).Distinct(StringComparer.Ordinal).ToArray();

    private static string Parent(string path)
    {
        int separator = path.LastIndexOf('/');
        return separator < 0 ? "" : path[..separator];
    }

    private static string Name(string path) => path[(path.LastIndexOf('/') + 1)..];

    // A root manifest (empty directory) does not own every repository path.
    private static bool Under(string path, string directory) => directory.Length != 0 &&
        (path == directory || path.StartsWith(directory + "/", StringComparison.Ordinal));

    [GeneratedRegex(@"^uv run --script ([A-Za-z0-9_./-]+\.py)$")]
    private static partial Regex UvScript();
}
