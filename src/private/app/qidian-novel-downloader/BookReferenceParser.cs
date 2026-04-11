using System.Text.RegularExpressions;

namespace Hcoona.QidianNovelDownloader;

internal static partial class BookReferenceParser
{
    [GeneratedRegex(@"^\d+$", RegexOptions.CultureInvariant)]
    private static partial Regex NumericBookIdRegex();

    [GeneratedRegex(
        @"^https://www\.qidian\.com/book/(?<bookId>\d+)/?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex CanonicalBookUrlRegex();

    public static BookReference Parse(string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            throw new CliInputException("A book id or canonical Qidian book URL is required.");
        }

        string trimmed = rawValue.Trim();
        if (NumericBookIdRegex().IsMatch(trimmed))
        {
            return new BookReference(trimmed, trimmed);
        }

        Match match = CanonicalBookUrlRegex().Match(trimmed);
        if (match.Success)
        {
            return new BookReference(trimmed, match.Groups["bookId"].Value);
        }

        throw new CliInputException(
            $"Unsupported book reference '{rawValue}'. Use a numeric book id or "
            + "https://www.qidian.com/book/{bookId}.");
    }
}
