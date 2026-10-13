namespace WorkflowDelivery.Repository;

internal static class DotNetGraphRequestValidation
{
    private static readonly string[] VariantSelectors = ["TargetFramework",
        "TargetFrameworks", "RuntimeIdentifier", "RuntimeIdentifiers"];

    internal static void RejectVariantSelectors(Dictionary<string, string> globals)
    {
        if (globals.Keys.Any(name => VariantSelectors.Contains(name,
            StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException(
                "Native graph discovery does not accept caller variant selectors.");
    }
}
