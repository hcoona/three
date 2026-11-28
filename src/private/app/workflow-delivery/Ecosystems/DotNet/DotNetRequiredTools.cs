namespace WorkflowDelivery.Repository;

// The bounded native item contract, independent of how CI later partitions checks.
internal static class DotNetRequiredTools
{
    internal const string Item = "ThreeRequiredTool";
    private static readonly string[] Supported = ["node", "npm", "python"];

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
}
