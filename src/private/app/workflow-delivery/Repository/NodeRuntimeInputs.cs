using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace WorkflowDelivery.Repository;

internal sealed record NodeRuntimeVariant(string Key, string Selector, string? ExactVersion);
internal sealed record NodeRuntimeInputs(string Revision, NodeRuntimeVariant[] Variants,
    string PnpmVersion, string[] Sources)
{
    internal CiPlan Expand(CiPlan plan)
    {
        if (plan.Candidate != Revision || plan.Scope != NodeFactsAssembler.Scope ||
            Variants.Length != 2 || Variants.Count(v => v.Selector == "22.x") != 1 ||
            Variants.Count(v => v.Selector == "24.x") != 1)
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
                !package || v.Selector == "24.x"))
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

// The existing workflow and native mise metadata own the runtime declarations.
internal sealed class NodeRuntimeReader
{
    internal const string Workflow = ".github/workflows/ci.yml";
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

    internal async Task<NodeRuntimeInputs> ReadAsync(CancellationToken token)
    {
        string? workflow = await checkout.ReadOptionalTextAsync(Workflow, token);
        foreach (string path in new[] { "mise.toml", "mise.lock" })
            if (await checkout.ReadOptionalTextAsync(path, token) is null)
                throw new InvalidDataException("Missing committed runtime authority: " + path);
        if (workflow is null)
            throw new InvalidDataException("Missing committed Node runtime workflow.");
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
            Field(matrix, "node-version") is not List<object?> values ||
            values.Count != 2 || values.OfType<string>().Distinct().Count() != 2 ||
            !values.Contains("22.x") || !values.Contains("24.x"))
            throw new InvalidDataException("Unsupported complete Node runtime matrix.");
        string node = await ToolAsync("node", "24", token);
        string pnpm = await ToolAsync("pnpm", null, token);
        if (!node.StartsWith("24.", StringComparison.Ordinal))
            throw new InvalidDataException("The configured Node runtime differs from its matrix.");
        NodeRuntimeVariant[] variants = values.Cast<string>().Select(selector => selector switch
        {
            "22.x" => new NodeRuntimeVariant("node22", selector, null),
            "24.x" => new NodeRuntimeVariant("node24", selector, node),
            _ => throw new InvalidDataException("Unsupported Node runtime."),
        }).ToArray();
        return new(checkout.Revision.Commit, variants, pnpm,
            [Workflow, "mise.toml", "mise.lock"]);
    }

    private async Task<string> ToolAsync(string tool, string? expectedRequest,
        CancellationToken token)
    {
        using JsonDocument json = JsonDocument.Parse(await query(
            ["ls", "--json", "--current", "--locked", tool], token));
        token.ThrowIfCancellationRequested();
        if (json.RootElement.ValueKind != JsonValueKind.Array ||
            json.RootElement.GetArrayLength() != 1)
            throw new InvalidDataException("Unresolved native runtime tool: " + tool);
        JsonElement value = json.RootElement[0];
        string version = Text(value, "version");
        string requested = Text(value, "requested_version");
        JsonElement source = value.GetProperty("source");
        if (!System.Version.TryParse(version, out System.Version? parsed) || parsed.Build < 0 ||
            (expectedRequest is not null && requested != expectedRequest) ||
            (expectedRequest is null && requested != version) ||
            value.GetProperty("active").ValueKind != JsonValueKind.True ||
            Text(source, "type") != "mise.toml" ||
            Text(source, "path") != Path.Combine(checkout.Root, "mise.toml"))
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
