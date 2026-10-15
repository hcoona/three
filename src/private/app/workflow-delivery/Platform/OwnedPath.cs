namespace WorkflowDelivery.Platform;

// Lexical checks for runner-supplied or application-created directories. These locations
// are trusted runner state; link aliases are not treated as a boundary. Containment
// remains for relative names crossing a transfer/process boundary and for separation
// requirements such as scratch outside the checkout.
internal static class OwnedPath
{
    internal static StringComparison Comparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal static string Absolute(string path, string message) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)
            ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(path))
            : throw new InvalidDataException(message);

    // True when child equals parent or lies below it; parent may be a filesystem root.
    internal static bool Within(string child, string parent) =>
        child.Equals(parent, Comparison) || child.StartsWith(
            Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar,
            Comparison);

    internal static bool Overlap(string first, string second) =>
        Within(first, second) || Within(second, first);
}
