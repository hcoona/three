namespace Hcoona.DocumentTranslatorCli;

internal static class MarkdownTextMetrics
{
    public static int CountUnicodeScalarValues(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.EnumerateRunes().Count();
    }

    public static bool IsValidUnicodeScalarSequence(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        for (int i = 0; i < text.Length; i++)
        {
            char current = text[i];
            if (char.IsHighSurrogate(current))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                {
                    return false;
                }

                i++;
                continue;
            }

            if (char.IsLowSurrogate(current))
            {
                return false;
            }
        }

        return true;
    }
}
