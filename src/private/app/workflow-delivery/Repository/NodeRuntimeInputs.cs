using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace WorkflowDelivery.Repository;

internal sealed record NodeRuntimeVariant(string Key, string Selector, string? ExactVersion);
internal sealed record NodeRuntimeInputs(string Revision, NodeRuntimeVariant[] Variants,
    string PnpmVersion, string[] Sources, string PackagingVariant, string ScheduleCommit,
    string NewestRelease, int LowerBound)
{
    internal CiPlan Expand(CiPlan plan)
    {
        if (plan.Candidate != Revision || plan.Scope != NodeFactsAssembler.Scope ||
            Variants.Length == 0 || Variants.Select(v => v.Key).Distinct().Count(
                ) != Variants.Length ||
            Variants.Count(v => v.Key == PackagingVariant && v.ExactVersion is not null) != 1)
            throw new InvalidDataException("Unresolved Node runtime plan subject.");
        // Validate the complete original plan before projecting its concrete variants.
        ResultCollector.Collect(plan, []);
        var checks = new List<PlannedCheck>();
        foreach (PlannedCheck item in plan.Checks)
        {
            CheckSpec work = item.Work;
            bool package = work.Key.Check is "node/npm-artifact-v1" or
                "node/npm-artifact-contents-v1" or "node/npm-install-import-v1";
            if (work.Key.Variant != "default" || work.Dimensions.Count != 0 ||
                work.Runner != "ubuntu-latest" || (!package && work.Key.Check is not
                    ("node/project-build-v1" or "node/project-test-v1")))
                throw new InvalidDataException("Unsupported Node runtime check projection.");
            foreach (NodeRuntimeVariant variant in Variants.Where(v =>
                !package || v.Key == PackagingVariant))
            {
                var dimensions = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["node"] = variant.Selector,
                    ["pnpm"] = PnpmVersion,
                };
                if (variant.ExactVersion is not null)
                    dimensions.Add("node-version", variant.ExactVersion);
                checks.Add(item with
                {
                    Work = work with
                    {
                        Key = work.Key with { Variant = variant.Key },
                        Dimensions = dimensions,
                        Prerequisites = work.Prerequisites.Select(p =>
                            p with { Variant = variant.Key }).ToArray(),
                    },
                });
            }
        }
        CiPlan expanded = plan with { Checks = [.. checks] };
        ResultCollector.Collect(expanded, []);
        return expanded;
    }
}

internal sealed record NodeEnginesManifest(string Path, string? Text);

// Committed release-schedule data, declared engines and native mise metadata own the
// runtime plan; the workflow only consumes the planned selectors.
internal sealed class NodeRuntimeReader
{
    internal const string Workflow = ".github/workflows/ci.yml";
    internal const string Schedule = "eng/node-release-schedule.json";
    internal const string MatrixConsumer =
        "${{ fromJSON(needs.node-group.outputs.node_versions) }}";
    private static readonly JsonDocumentOptions Strict = new() { AllowDuplicateProperties = false };
    private static readonly Regex ExactRelease = new(
        "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$", RegexOptions.CultureInvariant);
    private static readonly Regex Commit = new("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
    private static readonly Regex Release = new("^v(0\\.[0-9]+|[1-9][0-9]*)$",
        RegexOptions.CultureInvariant);
    private static readonly Regex Engines = new(
        "^>=(0|[1-9][0-9]*)(\\.(0|[1-9][0-9]*)){0,2}$", RegexOptions.CultureInvariant);
    private readonly GitMaterialization checkout;
    private readonly Func<string[], CancellationToken, Task<string>> query;

    internal NodeRuntimeReader(GitMaterialization checkout)
        : this(checkout, (arguments, token) => NativeProcess.RunAsync("mise", checkout.Root,
            arguments, token))
    { }

    internal NodeRuntimeReader(GitMaterialization checkout,
        Func<string[], CancellationToken, Task<string>> query)
    {
        this.checkout = checkout;
        this.query = query;
    }

    // Importers are the candidate's complete native PNPM workspace roster.
    internal async Task<NodeRuntimeInputs> ReadAsync(IEnumerable<string> importers,
        CancellationToken token)
    {
        string? workflow = await checkout.ReadOptionalTextAsync(Workflow, token);
        string? schedule = await checkout.ReadOptionalTextAsync(Schedule, token);
        string? mise = await checkout.ReadOptionalTextAsync("mise.toml", token);
        string? miseLock = await checkout.ReadOptionalTextAsync("mise.lock", token);
        var manifests = new List<NodeEnginesManifest>();
        foreach (string path in ManifestPaths(importers))
            manifests.Add(new(path, await checkout.ReadOptionalTextAsync(path, token)));
        // Committed rules are checked before each native query, as the pure core repeats.
        _ = Plan(workflow, schedule, mise, miseLock, manifests);
        string node = await query(["ls", "--json", "--current", "--locked", "node"], token);
        token.ThrowIfCancellationRequested();
        _ = Tool(checkout.Root, node, "node", false);
        string pnpm = await query(["ls", "--json", "--current", "--locked", "pnpm"], token);
        token.ThrowIfCancellationRequested();
        return Read(checkout.Revision, checkout.Root, workflow, schedule, mise, miseLock,
            manifests, node, pnpm);
    }

    internal static string[] ManifestPaths(IEnumerable<string> importers) => importers
        .Append(".").Distinct(StringComparer.Ordinal).Select(directory =>
        {
            string path = directory == "." ? "package.json" : directory + "/package.json";
            ImpactPlanner.ValidatePath(path);
            return path;
        }).Order(StringComparer.Ordinal).ToArray();

    // Pure runtime rules over committed declarations and the native mise answers.
    internal static NodeRuntimeInputs Read(GitRevision revision, string root, string? workflow,
        string? schedule, string? mise, string? miseLock,
        IReadOnlyList<NodeEnginesManifest> manifests, string nodeJson, string pnpmJson)
    {
        NodeRuntimePlan plan = Plan(workflow, schedule, mise, miseLock, manifests);
        string node = Tool(root, nodeJson, "node", false);
        string pnpm = Tool(root, pnpmJson, "pnpm", true);
        int packaging = System.Version.Parse(node).Major;
        if (!plan.Majors.Contains(packaging))
            throw new InvalidDataException(
                "The configured packaging runtime is absent from the matrix.");
        NodeRuntimeVariant[] variants = plan.Majors.Select(major => new NodeRuntimeVariant(
            "node" + Major(major), Major(major) + ".x", major == packaging ? node : null))
            .ToArray();
        return new(revision.Commit, variants, pnpm,
            [Schedule, "mise.toml", "mise.lock", .. manifests.Select(m => m.Path)],
            "node" + Major(packaging), plan.ScheduleCommit, plan.NewestRelease, plan.LowerBound);
    }

    private sealed record NodeRuntimePlan(int[] Majors, string ScheduleCommit,
        string NewestRelease, int LowerBound);

    private static NodeRuntimePlan Plan(string? workflow, string? schedule, string? mise,
        string? miseLock, IReadOnlyList<NodeEnginesManifest> manifests)
    {
        if (mise is null)
            throw new InvalidDataException("Missing committed runtime authority: mise.toml");
        if (miseLock is null)
            throw new InvalidDataException("Missing committed runtime authority: mise.lock");
        if (workflow is null)
            throw new InvalidDataException("Missing committed Node runtime workflow.");
        RequireMatrixConsumer(workflow);
        if (schedule is null)
            throw new InvalidDataException("Missing committed Node release schedule: " +
                Schedule);
        (string commit, string newest, Dictionary<int, bool> lts) = ReadSchedule(schedule);
        if (manifests.Count == 0 || !manifests.Any(m => m.Path == "package.json"))
            throw new InvalidDataException("Missing root Node engines manifest.");
        // Lowest minimum (union): every member runs on every planned major.
        int lower = manifests.Min(LowerMajor);
        int newestMajor = int.Parse(newest.Split('.')[0], CultureInfo.InvariantCulture);
        if (lower > newestMajor)
            throw new InvalidDataException(
                "The declared Node lower bound is above the newest release.");
        var majors = new List<int>();
        for (int major = lower; major <= newestMajor; major++)
        {
            if (!lts.TryGetValue(major, out bool designated))
                throw new InvalidDataException("The Node release schedule has no row: v" +
                    Major(major));
            if (designated || major == newestMajor) majors.Add(major);
        }
        return new([.. majors], commit, newest, lower);
    }

    private static void RequireMatrixConsumer(string workflow)
    {
        object? yaml;
        try
        {
            yaml = new DeserializerBuilder().WithDuplicateKeyChecking().Build()
                .Deserialize<object?>(workflow);
        }
        catch (YamlException error)
        {
            throw new InvalidDataException("Invalid Node runtime workflow YAML.", error);
        }
        object? matrix = Field(Field(Field(Field(yaml, "jobs"), "node-tests"),
            "strategy"), "matrix");
        if (matrix is not IDictionary<object, object?> { Count: 1 } ||
            Field(matrix, "node-version") is not string consumer ||
            consumer != MatrixConsumer)
            throw new InvalidDataException("Unsupported complete Node runtime matrix.");
    }

    private static (string Commit, string Newest, Dictionary<int, bool> Lts) ReadSchedule(
        string text)
    {
        const string invalid = "Invalid committed Node release schedule.";
        try
        {
            using JsonDocument json = JsonDocument.Parse(text, Strict);
            JsonElement data = json.RootElement;
            if (!Members(data, "newestRelease", "schedule") ||
                !Members(data.GetProperty("schedule"), "commit", "releases"))
                throw new InvalidDataException(invalid);
            JsonElement schedule = data.GetProperty("schedule");
            string newest = data.GetProperty("newestRelease").ValueKind ==
                JsonValueKind.String ? data.GetProperty("newestRelease").GetString()! : "";
            string commit = schedule.GetProperty("commit").ValueKind == JsonValueKind.String
                ? schedule.GetProperty("commit").GetString()! : "";
            JsonElement releases = schedule.GetProperty("releases");
            if (!ExactRelease.IsMatch(newest) || !Commit.IsMatch(commit) ||
                releases.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException(invalid);
            var lts = new Dictionary<int, bool>();
            foreach (JsonProperty row in releases.EnumerateObject())
            {
                if (!Release.IsMatch(row.Name) || row.Value.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException(invalid);
                bool designated = row.Value.TryGetProperty("lts", out JsonElement value) &&
                    value.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrEmpty(value.GetString());
                // Pre-1.0 lines (v0.x) are valid schedule rows but no planned major.
                if (!row.Name.StartsWith("v0.", StringComparison.Ordinal))
                    lts.Add(int.Parse(row.Name[1..], CultureInfo.InvariantCulture), designated);
            }
            return (commit, newest, lts);
        }
        catch (Exception error) when (error is JsonException or FormatException or
            OverflowException or ArgumentException)
        {
            throw new InvalidDataException(invalid, error);
        }
    }

    private static bool Members(JsonElement value, params string[] names) =>
        value.ValueKind == JsonValueKind.Object &&
        value.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)
            .SequenceEqual(names.Order(StringComparer.Ordinal), StringComparer.Ordinal);

    private static int LowerMajor(NodeEnginesManifest manifest)
    {
        if (manifest.Text is null)
            throw new InvalidDataException("Unreadable Node engines manifest: " + manifest.Path);
        string? range;
        try
        {
            using JsonDocument json = JsonDocument.Parse(manifest.Text, Strict);
            range = json.RootElement.ValueKind == JsonValueKind.Object &&
                json.RootElement.TryGetProperty("engines", out JsonElement engines) &&
                engines.ValueKind == JsonValueKind.Object &&
                engines.TryGetProperty("node", out JsonElement node) &&
                node.ValueKind == JsonValueKind.String ? node.GetString() : null;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException("Unreadable Node engines manifest: " +
                manifest.Path, error);
        }
        Match match = Engines.Match(range ?? "");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out int major))
            throw new InvalidDataException("Unsupported Node engines range: " + manifest.Path);
        return major;
    }

    private static string Major(int major) => major.ToString(CultureInfo.InvariantCulture);

    private static string Tool(string root, string answer, string tool, bool pinned)
    {
        using JsonDocument json = JsonDocument.Parse(answer);
        if (json.RootElement.ValueKind != JsonValueKind.Array ||
            json.RootElement.GetArrayLength() != 1)
            throw new InvalidDataException("Unresolved native runtime tool: " + tool);
        JsonElement value = json.RootElement[0];
        string version = Text(value, "version");
        string requested = Text(value, "requested_version");
        JsonElement source = value.GetProperty("source");
        if (!System.Version.TryParse(version, out System.Version? parsed) || parsed.Build < 0 ||
            (pinned ? requested != version :
                requested != version && requested != parsed.Major.ToString(
                    CultureInfo.InvariantCulture)) ||
            value.GetProperty("active").ValueKind != JsonValueKind.True ||
            Text(source, "type") != "mise.toml" ||
            Text(source, "path") != Path.Combine(root, "mise.toml"))
            throw new InvalidDataException("Native runtime tool is outside committed authority.");
        return version;
    }

    private static object? Field(object? value, string name) =>
        value is IDictionary<object, object?> fields && fields.TryGetValue(name, out object? field)
            ? field
            : throw new InvalidDataException("Missing Node runtime workflow field: " + name);

    private static string Text(JsonElement value, string name) =>
        value.TryGetProperty(name, out JsonElement text) &&
        text.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(text.GetString())
            ? text.GetString()! : throw new InvalidDataException("Missing native tool identity.");
}
