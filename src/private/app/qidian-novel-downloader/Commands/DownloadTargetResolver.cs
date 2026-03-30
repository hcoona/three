namespace Hcoona.QidianNovelDownloader.Commands;

internal static class DownloadTargetResolver
{
    public static List<BookReference> Resolve(
        IReadOnlyList<string> commandLineTargets,
        IReadOnlyList<string> configuredDefaultTargets)
    {
        IEnumerable<string> rawTargets = commandLineTargets.Count > 0
            ? commandLineTargets
            : configuredDefaultTargets;
        List<BookReference> targets = rawTargets
            .Select(BookReferenceParser.Parse)
            .DistinctBy(static reference => reference.BookId, StringComparer.Ordinal)
            .ToList();

        if (targets.Count == 0)
        {
            throw new CliInputException(
                "No book targets were provided. Supply book ids on the command line or configure "
                + "default books in the tool-managed config file.");
        }

        return targets;
    }
}
