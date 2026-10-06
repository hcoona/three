using System.Text.Json;
using WorkflowDelivery.CI;
using WorkflowDelivery.Platform;

namespace WorkflowDelivery.Repository;

internal sealed record ScopeResponsibility(string Owner, string Target, string Rule,
    string[] Sources);
internal sealed record ScopeCoordinate(string Path, bool Present, string? Mode,
    ScopeResponsibility[] Reasons);

// Responsibility is supplied by the retained selector. Consumption remains native facts.
internal sealed record ScopeInputs(CiComparison Comparison, string[] ChangedPaths,
    ScopeCoordinate[] Basis, ScopeCoordinate[] Candidate)
{
    internal static ScopeInputs Read(string text, GitComparison native, bool full)
    {
        using JsonDocument document = JsonDocument.Parse(text);
        JsonElement root = document.RootElement;
        var comparison = new CiComparison(Text(Field(root, "base")),
            Text(Field(root, "candidate")), Boolean(Field(root, "full")));
        if (comparison.Basis != native.Basis.Commit ||
            comparison.Candidate != native.Candidate.Commit || comparison.Full != full ||
            (full && comparison.Basis != comparison.Candidate))
            throw new InvalidDataException("Scope has a different native comparison.");
        string[] paths = Paths(Field(root, "changed_paths"));
        if (!paths.ToHashSet(StringComparer.Ordinal).SetEquals(native.ChangedPaths))
            throw new InvalidDataException("Scope omitted or added changed coordinates.");
        JsonElement endpoints = Field(root, "endpoint_owners");
        return new(comparison, paths, Endpoint(Field(endpoints, "basis"), native.Basis, paths),
            Endpoint(Field(endpoints, "candidate"), native.Candidate, paths));
    }

    internal PlanRequest Bind(RepositoryFacts basis, RepositoryFacts candidate)
    {
        if (basis.Revision != Comparison.Basis || candidate.Revision != Comparison.Candidate)
            throw new InvalidDataException("Scope and native consumption endpoints differ.");
        // Validate complete relations before any coordinate can become known unaffected.
        ImpactPlanner.SelectProjects(new(basis, candidate, [], false));
        var unaffected = new List<string>();
        var after = Candidate.ToDictionary(row => row.Path, StringComparer.Ordinal);
        foreach (ScopeCoordinate before in Basis)
        {
            ScopeCoordinate current = after[before.Path];
            bool beforeConsumes = ImpactPlanner.DirectConsumers(basis, before.Path).Length != 0;
            bool afterConsumes = ImpactPlanner.DirectConsumers(candidate, before.Path).Length != 0;
            if ((before.Present && before.Reasons.Length == 0 && !beforeConsumes) ||
                (current.Present && current.Reasons.Length == 0 && !afterConsumes))
                throw new InvalidDataException(
                    "Unresolved endpoint responsibility: " + before.Path);
            if (!beforeConsumes && !afterConsumes)
                unaffected.Add(before.Path);
        }
        string[] known = unaffected.Order(StringComparer.Ordinal).ToArray();
        return new(basis with { UnaffectedPaths = known },
            candidate with { UnaffectedPaths = known }, ChangedPaths, Comparison.Full);
    }

    private static ScopeCoordinate[] Endpoint(JsonElement value, GitRevision native,
        string[] changed)
    {
        if (Text(Field(value, "revision")) != native.Commit)
            throw new InvalidDataException("Scope responsibility has a different endpoint.");
        var entries = native.Entries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        var rows = new Dictionary<string, ScopeCoordinate>(StringComparer.Ordinal);
        foreach (JsonElement item in Array(Field(value, "paths")))
        {
            string path = Text(Field(item, "path"));
            ImpactPlanner.ValidatePath(path);
            bool present = Boolean(Field(item, "present"));
            JsonElement modeValue = Field(item, "mode");
            string? mode = modeValue.ValueKind == JsonValueKind.Null ? null : Text(modeValue);
            bool nativePresent = entries.TryGetValue(path, out GitEntry? entry);
            if (present != nativePresent || mode != entry?.Mode)
                throw new InvalidDataException("Scope entry differs from native Git: " + path);
            var reasons = new List<ScopeResponsibility>();
            foreach (JsonElement reason in Array(Field(item, "reasons")))
            {
                string[] sources = Paths(Field(reason, "sources"));
                if (!present || sources.Length == 0 || sources.Any(source =>
                    !entries.TryGetValue(source, out GitEntry? origin) ||
                    origin.ObjectType != "blob" || origin.Mode is not ("100644" or "100755")))
                    throw new InvalidDataException("Scope responsibility has no committed source.");
                reasons.Add(new(Text(Field(reason, "owner")), Text(Field(reason, "target")),
                    Text(Field(reason, "rule")), sources));
            }
            if (!rows.TryAdd(path, new(path, present, mode, reasons.ToArray())))
                throw new InvalidDataException("Scope repeated a changed coordinate.");
        }
        if (!rows.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(changed))
            throw new InvalidDataException("Scope endpoint omitted or added changed coordinates.");
        return rows.Values.OrderBy(row => row.Path, StringComparer.Ordinal).ToArray();
    }

    private static string[] Paths(JsonElement value)
    {
        string[] paths = Array(value).Select(Text).ToArray();
        foreach (string path in paths) ImpactPlanner.ValidatePath(path);
        if (paths.Distinct(StringComparer.Ordinal).Count() != paths.Length)
            throw new InvalidDataException("Scope repeated a path coordinate.");
        return paths;
    }

    private static JsonElement Field(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Invalid scope object.");
        JsonProperty[] fields = value.EnumerateObject().Where(field => field.Name == name)
            .ToArray();
        return fields.Length == 1 ? fields[0].Value
            : throw new InvalidDataException("Missing or duplicate scope field: " + name);
    }

    private static JsonElement.ArrayEnumerator Array(JsonElement value) =>
        value.ValueKind == JsonValueKind.Array ? value.EnumerateArray()
            : throw new InvalidDataException("Invalid scope array.");

    private static string Text(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()! : throw new InvalidDataException("Invalid scope text.");

    private static bool Boolean(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new InvalidDataException("Invalid scope Boolean."),
    };
}
