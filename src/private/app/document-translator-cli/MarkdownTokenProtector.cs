using System.Text.RegularExpressions;

namespace Hcoona.DocumentTranslatorCli;

internal static partial class MarkdownTokenProtector
{
    public static IReadOnlyList<ProtectedSlice> ScanEarlyMachineTokens(string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        return ScanEarlyMachineTokens(sourceText, new TextRange(0, sourceText.Length));
    }

    public static IReadOnlyList<ProtectedSlice> ScanEarlyMachineTokens(
        string sourceText,
        TextRange sourceRange)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        if (!sourceRange.IsWithin(sourceText))
        {
            throw new ArgumentOutOfRangeException(
                nameof(sourceRange),
                "The source range must be within the source text.");
        }

        List<ProtectedSlice> slices = [];
        string text = sourceText.Substring(sourceRange.Start, sourceRange.Length);
        foreach (Match match in EarlyMachineTokenRegex().Matches(text))
        {
            int absoluteStart = sourceRange.Start + match.Index;
            if (IsPathEmbeddedToken(sourceText, absoluteStart, match.Length))
            {
                continue;
            }

            slices.Add(new ProtectedSlice(
                SliceId: $"machine-token-{slices.Count}",
                Kind: MarkdownProtectedRangeKinds.MachineToken,
                SourceRange: new TextRange(absoluteStart, match.Length),
                OriginalText: match.Value));
        }

        return slices;
    }

    private static bool IsPathEmbeddedToken(string sourceText, int absoluteStart, int tokenLength)
    {
        int tokenEnd = absoluteStart + tokenLength;
        if (tokenLength <= 0 || tokenEnd > sourceText.Length)
        {
            return false;
        }

        return absoluteStart > 0 && IsPathTokenNeighborBefore(sourceText, absoluteStart - 1)
            || tokenEnd < sourceText.Length && IsPathTokenNeighborAfter(sourceText, tokenEnd);
    }

    private static bool IsPathTokenNeighborBefore(string sourceText, int index) =>
        sourceText[index] is '/' or '\\' or '.' or '~'
        || sourceText[index] == '-' && HasPathEvidenceBeforeHyphen(sourceText, index);

    private static bool HasPathEvidenceBeforeHyphen(string sourceText, int hyphenIndex)
    {
        for (int scan = hyphenIndex - 1; scan >= 0; scan--)
        {
            char value = sourceText[scan];
            if (char.IsWhiteSpace(value))
            {
                return false;
            }

            if (value is '/' or '\\')
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPathTokenNeighborAfter(string sourceText, int index) =>
        sourceText[index] is '/' or '\\' or '~'
        || sourceText[index] == '-' && HasPathEvidenceAfterHyphen(sourceText, index)
        || IsFileExtensionAfterToken(sourceText, index);

    private static bool HasPathEvidenceAfterHyphen(string sourceText, int hyphenIndex)
    {
        for (int scan = hyphenIndex + 1; scan < sourceText.Length; scan++)
        {
            char value = sourceText[scan];
            if (char.IsWhiteSpace(value))
            {
                return false;
            }

            if (value is '/' or '\\')
            {
                return true;
            }

            if (value == '.' && IsFileExtensionAfterToken(sourceText, scan))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsFileExtensionAfterToken(string sourceText, int index)
    {
        if (sourceText[index] != '.'
            || index + 1 >= sourceText.Length
            || !char.IsAsciiLetterOrDigit(sourceText[index + 1]))
        {
            return false;
        }

        for (int scan = index + 2; scan < sourceText.Length; scan++)
        {
            char value = sourceText[scan];
            if (char.IsWhiteSpace(value) || value is '/' or '\\')
            {
                break;
            }

            if (value is '.' or '-' or '_' || char.IsAsciiLetterOrDigit(value))
            {
                continue;
            }

            return true;
        }

        return true;
    }

    private const string EarlyMachineTokenPattern =
        """(?<![A-Za-z0-9_])(?:\$\{[A-Za-z_][A-Za-z0-9_]*\}"""
        + """|\{\{[^{}\r\n]+\}\}"""
        + """|\{[0-9]+\})(?![A-Za-z0-9_])""";

    [GeneratedRegex(EarlyMachineTokenPattern, RegexOptions.CultureInvariant)]
    private static partial Regex EarlyMachineTokenRegex();
}
