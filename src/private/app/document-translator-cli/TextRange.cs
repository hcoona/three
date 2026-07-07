namespace Hcoona.DocumentTranslatorCli;

internal readonly record struct TextRange
{
    public TextRange(int start, int length)
    {
        if (start < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "Start must be non-negative.");
        }

        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "Length must be non-negative.");
        }

        Start = start;
        Length = length;
    }

    public int Start { get; }

    public int Length { get; }

    public int End => checked(Start + Length);

    public bool IsWithin(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Start <= text.Length && Length <= text.Length - Start;
    }

    public bool IsOnUnicodeScalarBoundaries(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return IsWithin(text)
            && IsUnicodeScalarBoundary(text, Start)
            && IsUnicodeScalarBoundary(text, End);
    }

    private static bool IsUnicodeScalarBoundary(string text, int offset) =>
        offset == 0
        || offset == text.Length
        || !char.IsLowSurrogate(text[offset]);
}
