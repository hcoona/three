namespace Hcoona.AzureAuth.CredProvider.Platform.FileSystem;

internal static class FileSystemPathSemantics
{
    internal static bool UsesWindowsPaths(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        return fileSystem.IsPathFullyQualified(@"C:\");
    }

    internal static StringComparison GetComparison(IFileSystem fileSystem) =>
        UsesWindowsPaths(fileSystem)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    internal static StringComparer GetComparer(IFileSystem fileSystem) =>
        UsesWindowsPaths(fileSystem) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static string Combine(
        IFileSystem fileSystem,
        string firstSegment,
        params string[] additionalSegments
    )
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstSegment);
        ArgumentNullException.ThrowIfNull(additionalSegments);

        string result = firstSegment;
        foreach (string segment in additionalSegments)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(segment);
            result = fileSystem.GetFullPath(
                result.EndsWith('/') || result.EndsWith('\\')
                    ? result + segment.TrimStart('/', '\\')
                    : result + "/" + segment.TrimStart('/', '\\')
            );
        }

        return fileSystem.GetFullPath(result);
    }
}
