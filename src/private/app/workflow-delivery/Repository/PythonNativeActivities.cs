using System.Text.Json;

namespace WorkflowDelivery.Repository;

// Native request IDs associate scoped answers. UV still owns groups, markers and versions.
internal sealed record PythonNativeActivities(
    IReadOnlyDictionary<string, string[]> Groups,
    IReadOnlyDictionary<string, bool> Markers,
    IReadOnlyDictionary<string, bool> PythonConstraints)
{
    internal static PythonNativeActivities Read(PythonSupplementRequest request,
        PythonSupplementResult result)
    {
        try
        {
            JsonElement facts = result.Facts;
            if (facts.GetProperty("workspace_root").GetString() != request.WorkspaceRoot ||
                facts.GetProperty("interpreter").GetString() != request.Interpreter)
                throw new InvalidDataException("Python activity subject differs from its request.");
            var groups = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (JsonElement answer in facts.GetProperty("group_operations").EnumerateArray())
            {
                string[] selected = answer.GetProperty("groups").EnumerateArray()
                    .Select(Text).ToArray();
                if (selected.Distinct(StringComparer.Ordinal).Count() != selected.Length ||
                    !groups.TryAdd(Text(answer.GetProperty("id")), selected))
                    throw new InvalidDataException("Ambiguous Python native group answer.");
            }
            RequireIds(request.GroupOperations.Select(operation => operation.Id), groups.Keys);
            return new(groups,
                Activity("markers", request.Markers.Select(marker => marker.Id)),
                Activity("python_constraints",
                    request.PythonConstraints.Select(input => input.Id)));

            Dictionary<string, bool> Activity(string field, IEnumerable<string> requested)
            {
                var answers = new Dictionary<string, bool>(StringComparer.Ordinal);
                foreach (JsonElement answer in facts.GetProperty(field).EnumerateArray())
                    if (!answers.TryAdd(Text(answer.GetProperty("id")),
                        answer.GetProperty("active").GetBoolean()))
                        throw new InvalidDataException("Ambiguous Python native activity answer.");
                RequireIds(requested, answers.Keys);
                return answers;
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or
            InvalidOperationException or ArgumentException)
        {
            throw new InvalidDataException("Invalid Python native activity response.", exception);
        }
    }

    private static void RequireIds(IEnumerable<string> requested, IEnumerable<string> answered)
    {
        string[] ids = requested.ToArray();
        if (ids.Any(string.IsNullOrWhiteSpace) ||
            ids.Distinct(StringComparer.Ordinal).Count() != ids.Length ||
            !ids.ToHashSet(StringComparer.Ordinal).SetEquals(answered))
            throw new InvalidDataException("Python native activity identities are incomplete.");
    }

    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()!
            : throw new InvalidDataException("Invalid Python native activity identity.");
}
