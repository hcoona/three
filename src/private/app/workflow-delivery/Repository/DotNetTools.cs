using System.Text.Json;
using System.Text.RegularExpressions;
using WorkflowDelivery.CI;

namespace WorkflowDelivery.Repository;

internal sealed record DotNetToolGroup(string Key, Dictionary<string, string> Tools);

// External tools that a whole .NET test project declares. One tool set is one execution group.
internal static partial class DotNetTools
{
    internal const string Item = "ThreeRequiredTool";
    internal const string Dimension = "tools";
    internal const string None = "none";
    internal const string Bundled = "bundled";
    internal static readonly string[] Supported = ["node", "npm", "python"];

    // The declared names, sorted; unknown, repeated or npm without node fail closed.
    internal static string[] Names(IEnumerable<string> declared)
    {
        string[] names = declared.ToArray();
        string? unknown = names.FirstOrDefault(name => !Supported.Contains(name,
            StringComparer.Ordinal));
        if (unknown is not null)
            throw new InvalidDataException("Unsupported required external tool: " + unknown);
        if (names.Distinct(StringComparer.Ordinal).Count() != names.Length)
            throw new InvalidDataException("Repeated required external tool.");
        if (names.Contains("npm") && !names.Contains("node"))
            throw new InvalidDataException("Required external tool npm needs node.");
        return names.Order(StringComparer.Ordinal).ToArray();
    }

    internal static string Key(IEnumerable<string> names)
    {
        string[] sorted = Names(names);
        return sorted.Length == 0 ? None : string.Join('+', sorted);
    }

    // The planned version of each declared tool from the candidate's committed configuration.
    internal static Dictionary<string, string> Planned(string[] names,
        IReadOnlyDictionary<string, string> versions)
    {
        var tools = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string name in Names(names))
        {
            if (!versions.TryGetValue(name, out string? version))
                throw new InvalidDataException("Missing planned version for external tool " +
                    name + ".");
            Version(name, version);
            tools.Add(name, version);
        }
        return tools;
    }

    // A compact object in ordinal name order, so equal tool sets carry equal dimensions.
    internal static string Serialize(Dictionary<string, string> tools)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach ((string name, string version) in tools.OrderBy(pair => pair.Key,
                         StringComparer.Ordinal))
                writer.WriteString(name, version);
            writer.WriteEndObject();
        }
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    // Execution reading applies the planning name and version rules to the dimension.
    internal static Dictionary<string, string> Parse(string dimension)
    {
        Dictionary<string, string> tools = JsonSerializer.Deserialize(dimension,
            TransferJson.Default.DictionaryStringString)
            ?? throw new InvalidDataException("Missing external tool dimension.");
        Names(tools.Keys);
        foreach ((string name, string version) in tools)
            Version(name, version);
        if (Serialize(tools) != dimension)
            throw new InvalidDataException("Non-canonical external tool dimension.");
        return new(tools, StringComparer.Ordinal);
    }

    internal static void Version(string tool, string version)
    {
        if (tool == "npm" ? version != Bundled : !ExactVersion().IsMatch(version))
            throw new InvalidDataException("Malformed planned " + tool + " version '" +
                version + "'.");
    }

    // Package preset checks carry no tools and belong to the none group.
    internal static string Group(PlannedCheck check)
    {
        if (!check.Origins.SequenceEqual([CheckOrigin.NativeRetained]))
            return check.Work.Dimensions.ContainsKey(Dimension)
                ? throw new InvalidDataException("A package check carries external tools.")
                : None;
        if (!check.Work.Dimensions.TryGetValue(Dimension, out string? dimension))
            throw new InvalidDataException("Missing external tool dimension.");
        return Key(Parse(dimension).Keys);
    }

    // Each distinct group in the plan with its planned versions, plus none.
    internal static DotNetToolGroup[] Groups(CiPlan plan)
    {
        var groups = new SortedDictionary<string, Dictionary<string, string>>(
            StringComparer.Ordinal)
        { [None] = [] };
        foreach (PlannedCheck check in plan.Checks)
        {
            string key = Group(check);
            Dictionary<string, string> tools = key == None ? [] :
                Parse(check.Work.Dimensions[Dimension]);
            if (groups.TryGetValue(key, out Dictionary<string, string>? planned))
            {
                if (Serialize(planned) != Serialize(tools))
                    throw new InvalidDataException("External tool group " + key +
                        " has different planned versions.");
            }
            else groups.Add(key, tools);
        }
        return groups.Select(pair => new DotNetToolGroup(pair.Key, pair.Value)).ToArray();
    }

    internal static CiPlan Select(CiPlan plan, string key)
    {
        if (!Groups(plan).Any(group => group.Key == key))
            throw new InvalidDataException("Unknown planned external tool group: " + key);
        return plan with { Checks = plan.Checks.Where(check => Group(check) == key).ToArray() };
    }

    [GeneratedRegex("^[0-9]+\\.[0-9]+\\.[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex ExactVersion();
}
