namespace LiteAgensh;

internal static class Paths
{
    public static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    public static bool AreSame(string? first, string? second) =>
        first is not null
        && second is not null
        && Normalize(first).Equals(Normalize(second), StringComparison.OrdinalIgnoreCase);

    public static bool IsSameOrUnder(string candidate, string directory)
    {
        string normalizedCandidate = Normalize(candidate);
        string normalizedDirectory = Normalize(directory);
        if (normalizedCandidate.Equals(normalizedDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string prefix = Path.EndsInDirectorySeparator(normalizedDirectory)
            ? normalizedDirectory
            : normalizedDirectory + Path.DirectorySeparatorChar;
        return normalizedCandidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
