using System.Text.Json;

namespace WorkflowDelivery.Platform;

internal sealed record CiComparison(string Basis, string Candidate, bool Full);

internal static class GitHubComparison
{
    internal static CiComparison Resolve(string eventName, JsonElement payload,
        string testedCandidate)
    {
        string candidate = Commit(testedCandidate, "tested candidate");
        if (payload.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("The native event payload must be an object.");

        switch (eventName)
        {
            case "pull_request":
                JsonElement request = ObjectProperty(payload, "pull_request");
                return new(CommitProperty(ObjectProperty(request, "base"), "sha"),
                    candidate, false);
            case "merge_group":
                if (StringProperty(payload, "action") != "checks_requested")
                    throw new InvalidDataException("Unsupported merge-group action.");
                JsonElement group = ObjectProperty(payload, "merge_group");
                RequireCandidate(CommitProperty(group, "head_sha"), candidate);
                return new(CommitProperty(group, "base_sha"), candidate, false);
            case "push":
                JsonElement deleted = Property(payload, "deleted");
                if (deleted.ValueKind != JsonValueKind.False)
                    throw new InvalidDataException("Push comparison requires deleted=false.");
                RequireCandidate(CommitProperty(payload, "after"), candidate);
                return new(CommitProperty(payload, "before"), candidate, false);
            case "workflow_dispatch":
                return new(candidate, candidate, true);
            default:
                throw new InvalidDataException("Unsupported CI event.");
        }
    }

    private static void RequireCandidate(string endpoint, string candidate)
    {
        if (endpoint != candidate)
            throw new InvalidDataException(
                "Native event target differs from the tested candidate.");
    }

    private static string CommitProperty(JsonElement parent, string name) =>
        Commit(StringProperty(parent, name), name);

    private static string Commit(string value, string name)
    {
        if (string.IsNullOrEmpty(value) || value.Length is not (40 or 64) ||
            value.Any(c => !char.IsAsciiHexDigit(c)) || value.All(c => c == '0'))
            throw new InvalidDataException($"{name} requires a full nonzero Git commit identity.");
        return value.ToLowerInvariant();
    }

    private static JsonElement Property(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out JsonElement value))
            throw new InvalidDataException($"Native event is missing {name}.");
        return value;
    }

    private static JsonElement ObjectProperty(JsonElement parent, string name)
    {
        JsonElement value = Property(parent, name);
        if (value.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"Native event {name} must be an object.");
        return value;
    }

    private static string StringProperty(JsonElement parent, string name)
    {
        JsonElement value = Property(parent, name);
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"Native event {name} must be a string.");
        return value.GetString()!;
    }
}
