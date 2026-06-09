namespace Hcoona.DocumentTranslatorCli;

internal static class MarkdownTextMetrics
{
    public static int CountUnicodeScalarValues(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.EnumerateRunes().Count();
    }
}
