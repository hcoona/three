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
        Path.TrimEndingDirectorySeparator(FullPath(path, message));

    internal static string FullPath(string path, string message) =>
        !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path)
            ? Path.GetFullPath(path) : throw new InvalidDataException(message);

    internal static void RequireCanonical(string path, string message)
    {
        if (Absolute(path, message) != path) throw new InvalidDataException(message);
    }

    internal static void RequireRepositoryRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Contains(':') ||
            path.Split('/').Any(part => part is "" or "." or ".."))
            throw new InvalidDataException(
                $"Expected a normalized repository-relative path: {path}");
    }

    internal static string RelativeWithin(string root, string path, string message)
    {
        string relative = Path.GetRelativePath(root, path)
            .Replace(Path.DirectorySeparatorChar, '/');
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith("../", StringComparison.Ordinal))
            throw new InvalidDataException(message);
        return relative;
    }

    internal static (string Checkout, string Scratch) RequireFreshExternalScratch(
        string checkout, string scratch, string message, string? pathMessage = null)
    {
        checkout = Absolute(checkout, pathMessage ?? message);
        scratch = Absolute(scratch, pathMessage ?? message);
        if (!Directory.Exists(checkout) || Overlap(scratch, checkout) ||
            !Directory.Exists(scratch) || Directory.EnumerateFileSystemEntries(scratch).Any())
            throw new InvalidDataException(message);
        return (checkout, scratch);
    }

    internal static string ResolveWithin(string rootDirectory, string relativePath,
        string rootMessage, string escapeMessage)
    {
        string root = Absolute(rootDirectory, rootMessage);
        string full = Path.GetFullPath(Path.Combine(root, relativePath));
        return !full.Equals(root, Comparison) && Within(full, root)
            ? full
            : throw new InvalidDataException(escapeMessage);
    }

    // True when child equals parent or lies below it; parent may be a filesystem root.
    internal static bool Within(string child, string parent) =>
        child.Equals(parent, Comparison) || child.StartsWith(
            Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar,
            Comparison);

    internal static bool Overlap(string first, string second) =>
        Within(first, second) || Within(second, first);
}
